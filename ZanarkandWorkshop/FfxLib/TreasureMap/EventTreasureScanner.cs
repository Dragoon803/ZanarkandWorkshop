using FFXProjectEditor.FfxLib.Atel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record EventPosition(float X, float Y, float Z, int ScriptOffset, int FunctionIndex);

public enum ChestLocationConfidence
{
    NotAConfirmedChest,
    Unresolved,
    Conditional,
    Exact
}

public sealed record EventTreasureCandidate(
    string EventPath,
    string EventId,
    string FieldId,
    int WorkerIndex,
    IReadOnlyList<int> TreasureIds,
    IReadOnlyList<EventPosition> Positions,
    IReadOnlyList<int> ModelIds,
    bool UsesSilentGrant)
{
    public const int StandardChestModelId = 0x5002;
    public const int BlueChestModelId = 0x50AA;
    public bool HasSinglePosition => Positions.Count == 1;
    public IReadOnlyList<EventPosition> InitialPositions
    {
        get
        {
            EventPosition[] functionZero = Positions.Where(position => position.FunctionIndex == 0).ToArray();
            if (functionZero.Length > 0 || Positions.Count == 0) return functionZero;

            // Most fields place worker initialization in function 0. Some retail events instead
            // append the model/position initializer as the worker's final function, after its
            // interaction and reward functions. Prefer that final position-bearing function when
            // function 0 contains no position at all.
            int initializerFunction = Positions.Max(position => position.FunctionIndex);
            return Positions.Where(position => position.FunctionIndex == initializerFunction).ToArray();
        }
    }
    public bool HasSingleInitialPosition => InitialPositions.Count == 1;
    public bool HasSingleTreasure => TreasureIds.Count == 1;
    public bool HasChestModel => ModelIds.Any(id => id is StandardChestModelId or BlueChestModelId);
    public bool IsDirectlyMappable => HasChestModel && HasSingleInitialPosition && HasSingleTreasure;
    public ChestLocationConfidence LocationConfidence =>
        !HasChestModel ? ChestLocationConfidence.NotAConfirmedChest :
        !HasSingleTreasure || InitialPositions.Count == 0 ? ChestLocationConfidence.Unresolved :
        InitialPositions.Count == 1 ? ChestLocationConfidence.Exact :
        ChestLocationConfidence.Conditional;
}

public sealed record EventTreasureScanResult(
    string EventPath,
    string EventId,
    IReadOnlyList<EventTreasureCandidate> Candidates,
    int WorkerCount,
    int StatementCount);

public static partial class EventTreasureScanner
{
    public static EventTreasureScanResult Scan(string eventPath)
    {
        EventPackage package = EventPackage.Read(eventPath);
        if (package.AtelBytes.Length == 0)
            throw new InvalidDataException("The event package has no ATEL chunk.");
        AtelScriptDocument document = AtelScriptDocument.Read(package.AtelBytes);
        string eventId = System.IO.Path.GetFileNameWithoutExtension(eventPath).ToLowerInvariant();

        var workers = new Dictionary<int, WorkerEvidence>();
        foreach (AtelStatement statement in document.Statements)
        {
            int workerIndex = document.GetWorkerIndexForCodeOffset(statement.Offset);
            if (workerIndex < 0) continue;
            AtelWorker worker = document.Workers[workerIndex];
            int functionIndex = GetFunctionIndex(worker, statement.Offset);
            if (!workers.TryGetValue(workerIndex, out WorkerEvidence? evidence))
            {
                evidence = new WorkerEvidence();
                workers.Add(workerIndex, evidence);
            }

            bool resolvedPositionDirectly = false;
            for (int instructionIndex = 0; instructionIndex < statement.Instructions.Count; instructionIndex++)
            {
                AtelInstruction call = statement.Instructions[instructionIndex];
                if (call.Opcode is not (0xB5 or 0xD8)) continue;
                if (call.Operand == 0x0013 && instructionIndex >= 3 &&
                    TryResolveFloat(statement.Instructions[instructionIndex - 3], worker, out float x) &&
                    TryResolveFloat(statement.Instructions[instructionIndex - 2], worker, out float y) &&
                    TryResolveFloat(statement.Instructions[instructionIndex - 1], worker, out float z))
                {
                    evidence.Positions.Add(new EventPosition(x, y, z, statement.Offset, functionIndex));
                    resolvedPositionDirectly = true;
                }

                if (call.Operand is 0x0001 or 0x0134 && instructionIndex >= 1 &&
                    TryResolveInteger(statement.Instructions[instructionIndex - 1], worker, out int modelId))
                    evidence.ModelIds.Add(modelId);

                if (call.Operand is 0x015B or 0x01A7 && instructionIndex >= 1 &&
                    TryResolveInteger(statement.Instructions[instructionIndex - 1], worker, out int treasureId))
                {
                    evidence.TreasureIds.Add(treasureId);
                    evidence.UsesSilentGrant |= call.Operand == 0x01A7;
                }
            }

            // Retain a translation fallback for expressions that the direct push/call pass can simplify.
            Match position = PositionRegex().Match(statement.Translation);
            if (!resolvedPositionDirectly && position.Success &&
                TryFloat(position.Groups[1].Value, out float fallbackX) &&
                TryFloat(position.Groups[2].Value, out float fallbackY) &&
                TryFloat(position.Groups[3].Value, out float fallbackZ))
                evidence.Positions.Add(new EventPosition(fallbackX, fallbackY, fallbackZ, statement.Offset, functionIndex));

            foreach (Match treasure in TreasureRegex().Matches(statement.Translation))
                if (int.TryParse(treasure.Groups[2].Value,
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int fallbackTreasureId))
                    evidence.TreasureIds.Add(fallbackTreasureId);
        }

        EventTreasureCandidate[] candidates = workers
            .Where(pair => pair.Value.TreasureIds.Count > 0)
            .Select(pair => new EventTreasureCandidate(
                eventPath,
                eventId,
                FieldAssetDiscovery.ToFieldId(eventId),
                pair.Key,
                pair.Value.TreasureIds.Distinct().OrderBy(id => id).ToArray(),
                pair.Value.Positions
                    .GroupBy(position => (position.X, position.Y, position.Z))
                    .Select(group => group.OrderBy(position => position.ScriptOffset).First())
                    .OrderBy(position => position.ScriptOffset)
                    .ToArray(),
                pair.Value.ModelIds.Distinct().OrderBy(id => id).ToArray(),
                pair.Value.UsesSilentGrant))
            .OrderBy(candidate => candidate.WorkerIndex)
            .ToArray();

        return new EventTreasureScanResult(
            eventPath, eventId, candidates, document.Workers.Count, document.Statements.Count);
    }

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static int GetFunctionIndex(AtelWorker worker, int scriptOffset)
    {
        int result = -1;
        int bestOffset = -1;
        for (int index = 0; index < worker.FunctionOffsets.Count; index++)
        {
            int offset = worker.FunctionOffsets[index];
            if (offset <= scriptOffset && offset >= bestOffset)
            {
                bestOffset = offset;
                result = index;
            }
        }
        return result;
    }

    private static bool TryResolveInteger(AtelInstruction instruction, AtelWorker worker, out int value)
    {
        if (instruction.Opcode == 0xAE)
        {
            value = unchecked((short)instruction.Operand);
            return true;
        }
        if (instruction.Opcode == 0xAD && instruction.Operand < worker.IntegerConstantValues.Count)
        {
            value = worker.IntegerConstantValues[instruction.Operand];
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryResolveFloat(AtelInstruction instruction, AtelWorker worker, out float value)
    {
        if (instruction.Opcode == 0xAF && instruction.Operand < worker.FloatConstantBits.Count)
        {
            value = BitConverter.Int32BitsToSingle(worker.FloatConstantBits[instruction.Operand]);
            return true;
        }
        if (instruction.Opcode == 0xAE)
        {
            value = unchecked((short)instruction.Operand);
            return true;
        }
        value = 0;
        return false;
    }

    [GeneratedRegex(@"Event\.setPosition \[0x0013\]\(x=(-?\d+(?:\.\d+)?)(?: \[[^]]+\])?, y=(-?\d+(?:\.\d+)?)(?: \[[^]]+\])?, z=(-?\d+(?:\.\d+)?)(?: \[[^]]+\])?\)")]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"Event\.(obtainTreasure(?:Silently)?) \[0x(?:015B|01A7)\]\([^)]*treasureId=(-?\d+)")]
    private static partial Regex TreasureRegex();

    private sealed class WorkerEvidence
    {
        public List<int> TreasureIds { get; } = [];
        public List<EventPosition> Positions { get; } = [];
        public List<int> ModelIds { get; } = [];
        public bool UsesSilentGrant { get; set; }
    }
}
