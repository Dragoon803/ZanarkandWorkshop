using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Encoding;
using FFXProjectEditor.FfxLib.Monster;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal class MonEditor_DataModel
    {
        public MonEditorSelector_DataModel SelectorDM { get; set; }
        public Monster_File MonsterFile { get; set; }
        public string MonsterPath { get; set; }
        public MonsterStatSheet_Wrapper MonsterStatSheet { get; set; }
        public MonsterLoot_Wrapper MonsterLoot { get; set; }
        public AtelScriptDocument? AiDocument { get; private set; }
        private readonly byte[] _originalAiBytes;
        private readonly byte[] _originalWorkerBytes;
        private readonly int _monsterId;
        private bool _usesLocalizedMonsterText;
        private readonly Stack<AiUndoSnapshot> _aiUndoHistory = new();
        private readonly Stack<AiUndoSnapshot> _aiRedoHistory = new();
        private sealed record AiUndoSnapshot(byte[] AiBytes, byte[] WorkerBytes, string Description,
            string? SelectionKind, int? ScriptOffset, string EditorHex);
        public string? LastUndoneSelectionKind { get; private set; }
        public int? LastUndoneScriptOffset { get; private set; }
        public string AiHex { get; set; } = "";
        public string AiSearchHex { get; set; } = "";
        public string AiReplacementHex { get; set; } = "";
        public string AiStatus { get; private set; } = "Battle Script not loaded.";
        public IReadOnlyList<int> AiSearchOffsets { get; private set; } = [];
        public int AiSearchLength { get; private set; }
        public IEnumerable<string> AiWorkers => AiDocument?.Workers.Select(w => w.Display) ?? [];
        public IEnumerable<AtelInstruction> AiInstructions => AiDocument?.Instructions ?? [];
        public IEnumerable<AtelStatement> AiStatements => AiDocument?.Statements ?? [];
        public IReadOnlyDictionary<ushort, string> AiCommandNames { get; private set; } = new Dictionary<ushort, string>();
        public string AiSummary => AiDocument == null
            ? "No readable Battle Script"
            : $"Script {AiDocument.ScriptId} | Creator: {AiDocument.Creator} | Code: 0x{AiDocument.ScriptCodeLength:X} bytes at 0x{AiDocument.ScriptCodeOffset:X} | Workers: {AiDocument.WorkerCount} | Actors: {AiDocument.ActorCount}";

        public List<string> CategoryOptions => new GameCategory_Converter().Options.Values.ToList();
        public IReadOnlyList<MonsterLootItemOption> LootItemOptions { get; private set; } = [];

        public MonEditor_DataModel(Monster_File monsterFile, string monsterPath, MonEditorSelector_DataModel selectorDM)
        {
            MonsterFile = monsterFile;
            MonsterPath = monsterPath;
            SelectorDM = selectorDM;
            _monsterId = ParseMonsterId(monsterPath);
            MonsterStatSheet = MonsterStatSheet_Wrapper.Wrap(MonsterFile.StatSheetFile);
            _usesLocalizedMonsterText = LoadLocalizedTextIntoWrapper();
            AiCommandNames = LoadCommandNames();
            MonsterLoot = MonsterLoot_Wrapper.Wrap(MonsterFile.LootFile, AiCommandNames);
            LootItemOptions = BuildLootItemOptions();
            _originalAiBytes = MonsterFile.AiFile == null ? [] : (byte[])MonsterFile.AiFile.Clone();
            _originalWorkerBytes = MonsterFile.WorkerFile == null ? [] : (byte[])MonsterFile.WorkerFile.Clone();

            try
            {
                AiDocument = AtelScriptDocument.Read(MonsterFile.AiFile, MonsterFile.WorkerFile);
                AiDocument.SetCommandNameResolver(gameIndex => AiCommandNames.TryGetValue(gameIndex, out string? name) ? name : null);
                AiHex = AiDocument.ToHexEditorText();
                AiStatus = AiDocument.RecoveredMissingCodeLength
                    ? $"Recovered missing ATEL code length as 0x{AiDocument.ScriptCodeLength:X}. Saving will repair the header."
                    : "Parsed successfully. Fixed-layout edits are enabled.";
            }
            catch (Exception ex)
            {
                AiStatus = "Battle Script parsing failed: " + ex.Message;
            }
        }

        public void RestoreOriginalAi()
        {
            if (_originalAiBytes.Length == 0)
                throw new InvalidOperationException("This monster had no Battle Script when it was opened.");
            AtelScriptDocument restored = AtelScriptDocument.Read(_originalAiBytes, _originalWorkerBytes);
            restored.SetCommandNameResolver(gameIndex => AiCommandNames.TryGetValue(gameIndex, out string? name) ? name : null);
            AiDocument = restored;
            MonsterFile.AiFile = (byte[])_originalAiBytes.Clone();
            MonsterFile.WorkerFile = (byte[])_originalWorkerBytes.Clone();
            AiHex = restored.ToHexEditorText();
            AiSearchOffsets = [];
            AiSearchLength = 0;
            AiStatus = "Restored the complete Battle Script to the state loaded when this monster was opened. Press Save to write the restored Battle Script to disk.";
        }

        public void RestoreOriginalAiAndSave()
        {
            RestoreOriginalAi();

            // Start from the file currently on disk so this operation cannot
            // accidentally commit pending stat, loot, or other editor changes.
            Monster_File diskMonster = Monster_File.Read(File.ReadAllBytes(MonsterPath));
            diskMonster.AiFile = (byte[])_originalAiBytes.Clone();
            diskMonster.WorkerFile = (byte[])_originalWorkerBytes.Clone();

            byte[] rebuilt = diskMonster.Write();
            Monster_File roundTrip = Monster_File.Read(rebuilt);
            AtelScriptDocument.Read(roundTrip.AiFile, roundTrip.WorkerFile);

            File.WriteAllBytes(MonsterPath, rebuilt);

            AiStatus = "Reverted the Battle Script to the state captured when this monster was opened and saved it to disk.";
        }

        public int AiUndoCount => _aiUndoHistory.Count;
        public int AiRedoCount => _aiRedoHistory.Count;

        public void RecordAiUndoCheckpoint(string description, string? selectionKind = null, int? scriptOffset = null)
        {
            if (AiDocument == null) return;
            _aiUndoHistory.Push(CaptureAiSnapshot(description, selectionKind, scriptOffset));
            _aiRedoHistory.Clear();
        }

        public void ApplyAiHexTransactional(string description, string? selectionKind, int? scriptOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AiUndoSnapshot before = CaptureAiSnapshot(description, selectionKind, scriptOffset);
            try
            {
                ApplyAiHex();
                _aiUndoHistory.Push(before);
                _aiRedoHistory.Clear();
            }
            catch
            {
                RestoreAiSnapshot(before);
                throw;
            }
        }

        public void ClearAiRedoHistory() => _aiRedoHistory.Clear();

        private AiUndoSnapshot CaptureAiSnapshot(string description, string? selectionKind, int? scriptOffset,
            string? editorHex = null)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            return new AiUndoSnapshot(
                (byte[])AiDocument.Bytes.Clone(),
                MonsterFile.WorkerFile == null ? [] : (byte[])MonsterFile.WorkerFile.Clone(),
                description, selectionKind, scriptOffset, editorHex ?? AiDocument.ToHexEditorText());
        }

        private void RestoreAiSnapshot(AiUndoSnapshot snapshot)
        {
            AtelScriptDocument restored = AtelScriptDocument.Read(snapshot.AiBytes, snapshot.WorkerBytes);
            restored.SetCommandNameResolver(gameIndex => AiCommandNames.TryGetValue(gameIndex, out string? name) ? name : null);
            AiDocument = restored;
            MonsterFile.AiFile = (byte[])snapshot.AiBytes.Clone();
            MonsterFile.WorkerFile = (byte[])snapshot.WorkerBytes.Clone();
            AiHex = snapshot.EditorHex;
            AiSearchOffsets = [];
            AiSearchLength = 0;
            LastUndoneSelectionKind = snapshot.SelectionKind;
            LastUndoneScriptOffset = snapshot.ScriptOffset;
        }

        public void UndoLastAiChange()
        {
            if (AiDocument == null)
                throw new InvalidOperationException(AiStatus);

            // Direct edits in the hex box do not alter AiDocument until they
            // are validated. Treat that pending text as the newest undoable
            // action before consulting the parsed-document history.
            bool hasPendingManualEdit;
            try
            {
                byte[] pendingBytes = AtelScriptDocument.ParseHexEditorText(AiHex);
                hasPendingManualEdit = !pendingBytes.AsSpan().SequenceEqual(AiDocument.Bytes);
            }
            catch
            {
                // Malformed or incomplete hex is also an unvalidated edit.
                hasPendingManualEdit = true;
            }
            if (hasPendingManualEdit)
            {
                _aiRedoHistory.Push(CaptureAiSnapshot("pending manual hex edit", null, null, AiHex));
                AiHex = AiDocument.ToHexEditorText();
                AiSearchOffsets = [];
                AiSearchLength = 0;
                AiStatus = $"Undid the pending manual hex edit and restored the last valid Battle Script. {_aiUndoHistory.Count} earlier change(s) remain available.";
                LastUndoneSelectionKind = null;
                LastUndoneScriptOffset = null;
                return;
            }

            byte[] current = AiDocument.Bytes;
            AiUndoSnapshot? snapshot = null;
            while (_aiUndoHistory.Count > 0)
            {
                AiUndoSnapshot candidate = _aiUndoHistory.Pop();
                if (!candidate.AiBytes.AsSpan().SequenceEqual(current))
                {
                    snapshot = candidate;
                    break;
                }
            }
            if (snapshot == null)
                throw new InvalidOperationException("There are no Battle Script changes left to undo in this session.");

            _aiRedoHistory.Push(CaptureAiSnapshot(snapshot.Description, snapshot.SelectionKind, snapshot.ScriptOffset));
            RestoreAiSnapshot(snapshot);
            AiStatus = $"Undid: {snapshot.Description}. {_aiUndoHistory.Count} earlier change(s) remain available.";
        }

        public void RedoLastAiChange()
        {
            if (AiDocument == null)
                throw new InvalidOperationException(AiStatus);
            if (_aiRedoHistory.Count == 0)
                throw new InvalidOperationException("There are no Battle Script changes available to redo in this session.");

            AiUndoSnapshot snapshot = _aiRedoHistory.Pop();
            _aiUndoHistory.Push(CaptureAiSnapshot(snapshot.Description, snapshot.SelectionKind, snapshot.ScriptOffset));
            RestoreAiSnapshot(snapshot);
            AiStatus = $"Redid: {snapshot.Description}. {_aiRedoHistory.Count} later change(s) remain available.";
        }

        public void StageVanillaMonster(string vanillaPath)
        {
            if (!File.Exists(vanillaPath))
                throw new InvalidOperationException($"Original monster file was not found: {vanillaPath}");
            Monster_File vanilla = Monster_File.Read(File.ReadAllBytes(vanillaPath));
            if (vanilla.AiFile == null || vanilla.AiFile.Length == 0)
                throw new InvalidOperationException("The selected original monster contains no Battle Script.");
            AtelScriptDocument vanillaAi = AtelScriptDocument.Read(vanilla.AiFile, vanilla.WorkerFile);
            vanillaAi.SetCommandNameResolver(gameIndex => AiCommandNames.TryGetValue(gameIndex, out string? name) ? name : null);

            MonsterFile = vanilla;
            MonsterStatSheet = MonsterStatSheet_Wrapper.Wrap(vanilla.StatSheetFile);
            _usesLocalizedMonsterText = LoadLocalizedTextIntoWrapper();
            MonsterLoot = MonsterLoot_Wrapper.Wrap(vanilla.LootFile, AiCommandNames);
            LootItemOptions = BuildLootItemOptions();
            AiDocument = vanillaAi;
            AiHex = vanillaAi.ToHexEditorText();
            AiSearchOffsets = [];
            AiSearchLength = 0;
            AiStatus = $"Staged the complete original monster from {vanillaPath}. The Battle Script, stats, affinities, rewards, loot, text, audio, and all other sections will be replaced when you press Save.";
        }

        public void RestoreOriginalMonsterAndSave(string originalPath)
        {
            StageVanillaMonster(originalPath);
            Save();
            AiStatus = $"Restored the complete original monster from {originalPath} and saved it to disk.";
        }

        private static IReadOnlyDictionary<ushort, string> LoadCommandNames()
        {
            var names = new Dictionary<ushort, string>();
            LoadCommandNames(names, Project_Service.Instance.Path_KernelItemUs, 0x2, true);
            LoadCommandNames(names, Project_Service.Instance.Path_KernelCommandUs, 0x3, true);
            LoadCommandNames(names, Project_Service.Instance.Path_KernelMonMagic1Us, 0x4, false);
            LoadCommandNames(names, Project_Service.Instance.Path_KernelMonMagic2Us, 0x6, false);
            return names;
        }

        private IReadOnlyList<MonsterLootItemOption> BuildLootItemOptions()
        {
            List<MonsterLootItemOption> options =
            [
                new(0x00FF, "NONE"),
                new(0x0000, "NONE")
            ];

            IEnumerable<ushort> itemIndexes = Item_Dictionary.Instance.Keys;
            itemIndexes = itemIndexes.Concat(
                AiCommandNames.Keys
                    .Where(itemId => (itemId & 0xF000) == 0x2000)
                    .Select(itemId => (ushort)(itemId & 0x0FFF)));

            foreach (ushort itemIndex in itemIndexes.Distinct().OrderBy(index => index))
            {
                ushort itemId = (ushort)(0x2000 | itemIndex);
                string displayName =
                    AiCommandNames.TryGetValue(itemId, out string? currentName)
                        ? currentName
                        : Item_Dictionary.Instance.TryGetValue(itemIndex, out string? fallbackName)
                            ? fallbackName
                            : $"Item {itemIndex}";
                options.Add(new(itemId, displayName));
            }

            ushort[] existingIds =
            [
                MonsterLoot.Drop1.Value, MonsterLoot.Drop1Rare.Value,
                MonsterLoot.Drop2.Value, MonsterLoot.Drop2Rare.Value,
                MonsterLoot.DropOverkill1.Value, MonsterLoot.DropOverkill1Rare.Value,
                MonsterLoot.DropOverkill2.Value, MonsterLoot.DropOverkill2Rare.Value,
                MonsterLoot.Steal.Value, MonsterLoot.StealRare.Value,
                MonsterLoot.Bribe.Value
            ];
            foreach (ushort existingId in existingIds.Distinct())
            {
                if (options.All(option => option.ItemId != existingId))
                    options.Insert(2, new(existingId, $"Unknown (0x{existingId:X4})"));
            }

            return options;
        }

        private static void LoadCommandNames(Dictionary<ushort, string> names, string path, int category, bool hasExtraInfo)
        {
            if (!File.Exists(path)) return;
            List<Ability_Command> commands = Ability_Command.ReadList(File.ReadAllBytes(path), hasExtraInfo);
            for (int index = 0; index < commands.Count && index <= 0xFFF; index++)
            {
                string name = FfxEncoding.DecodeScript(commands[index].NameScriptBytes).GetString(FfxEncoding.UsDecoder);
                if (!string.IsNullOrWhiteSpace(name)) names[(ushort)((category << 12) | index)] = name;
            }
        }

        public void ApplyAiHex()
        {
            if (AiDocument == null)
                throw new InvalidOperationException(AiStatus);

            byte[] editedBytes = AtelScriptDocument.ParseHexEditorText(AiHex);
            string structuralResult = "";
            if (editedBytes.Length == AiDocument.Bytes.Length)
            {
                AtelScriptDocument staged = AtelScriptDocument.Read(editedBytes, MonsterFile.WorkerFile);
                int[] existingReturns = AiDocument.Instructions
                    .Where(instruction => instruction.Opcode == 0x3C)
                    .Select(instruction => instruction.Offset).ToArray();
                int[] editedReturns = staged.Instructions
                    .Where(instruction => instruction.Opcode == 0x3C)
                    .Select(instruction => instruction.Offset).ToArray();
                if (!existingReturns.SequenceEqual(editedReturns))
                    throw new InvalidOperationException(
                        "Manual hex changes cannot add, remove, or move RETURN (3C). Edit the surrounding instructions while leaving each RETURN in place.");
                AiDocument.ReplaceBytes(editedBytes);
            }
            else if (editedBytes.Length < AiDocument.Bytes.Length)
            {
                structuralResult = ApplyManualCodeDeletion(editedBytes);
            }
            else
            {
                structuralResult = ApplyManualCodeInsertion(editedBytes);
            }
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = string.IsNullOrEmpty(structuralResult)
                ? $"Validated {editedBytes.Length} Battle Script bytes and {AiDocument.Instructions.Count} instructions."
                : structuralResult;
        }

        private string ApplyManualCodeDeletion(byte[] shortenedBytes)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            byte[] original = AiDocument.Bytes;
            int removedLength = original.Length - shortenedBytes.Length;
            (int prefix, int suffix) = FindSingleContiguousDifference(original, shortenedBytes);
            if (prefix + suffix != shortenedBytes.Length)
                throw new InvalidOperationException("Manual size changes must be one contiguous insertion or deletion. Use the structured controls for multiple regions.");
            int removalEnd = checked(prefix + removedLength);
            int codeStart = AiDocument.ScriptCodeOffset;
            int codeEnd = checked(codeStart + AiDocument.ScriptCodeLength);
            if (prefix < codeStart || removalEnd > codeEnd)
                throw new InvalidOperationException("Manual deletion may remove only complete statements inside the script-code region.");

            int scriptOffset = prefix - codeStart;
            AtelStatement[] overlappingStatements = AiDocument.Statements.Where(statement =>
                statement.Offset < scriptOffset + removedLength &&
                statement.Offset + statement.ByteLength > scriptOffset).ToArray();
            if (overlappingStatements.Length == 1)
            {
                AtelStatement impacted = overlappingStatements[0];
                bool removesWholeStatement = scriptOffset == impacted.Offset && removedLength == impacted.ByteLength;
                if (!removesWholeStatement)
                {
                    string deletedBytes = string.Join(' ', original.AsSpan(prefix, removedLength).ToArray().Select(value => value.ToString("X2")));
                    throw new ManualAiPartialStatementException(impacted.Offset, codeStart + impacted.Offset,
                        impacted.ByteLength - removedLength, deletedBytes, impacted.Translation);
                }
            }
            int cursor = scriptOffset;
            var removedStatements = new List<AtelStatement>();
            while (cursor < scriptOffset + removedLength)
            {
                AtelStatement statement = AiDocument.Statements.FirstOrDefault(item => item.Offset == cursor)
                    ?? throw new InvalidOperationException($"Manual deletion begins or ends inside an instruction/statement near 0x{cursor:X4}.");
                removedStatements.Add(statement);
                cursor += statement.ByteLength;
            }
            if (cursor != scriptOffset + removedLength)
                throw new InvalidOperationException("Manual deletion ends inside a Script Instruction or Battle Logic statement.");

            foreach (AtelStatement _ in removedStatements)
                AiDocument.DeleteStatement(scriptOffset);
            return $"Validated manual deletion of {removedLength} code byte(s) ({removedStatements.Count} complete statement(s)) at 0x{scriptOffset:X4}. " +
                $"ATEL padding, code length, and later offsets were rebuilt; {AiDocument.Instructions.Count} instructions remain.";
        }

        private string ApplyManualCodeInsertion(byte[] expandedBytes)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            byte[] original = AiDocument.Bytes;
            int insertedLength = expandedBytes.Length - original.Length;
            (int prefix, int suffix) = FindSingleContiguousDifference(expandedBytes, original);
            if (prefix + suffix != original.Length)
                throw new InvalidOperationException("Manual size changes must be one contiguous insertion or deletion. Use the structured controls for multiple regions.");
            int codeStart = AiDocument.ScriptCodeOffset;
            int codeEnd = checked(codeStart + AiDocument.ScriptCodeLength);
            if (prefix < codeStart || prefix > codeEnd)
                throw new InvalidOperationException("Manual insertion may add code only at an existing script instruction boundary.");
            int scriptOffset = prefix - codeStart;
            byte[] inserted = expandedBytes.AsSpan(prefix, insertedLength).ToArray();
            ValidateManualInsertedInstructions(scriptOffset, inserted);
            AiDocument.InsertStatementBytes(scriptOffset, inserted);
            return $"Validated manual insertion of {insertedLength} code byte(s) at 0x{scriptOffset:X4}. " +
                $"ATEL storage and code offsets were rebuilt; {AiDocument.Instructions.Count} instructions are now present.";
        }

        private void ValidateManualInsertedInstructions(int scriptOffset, byte[] inserted)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            if (scriptOffset < AiDocument.ScriptCodeLength && AiDocument.Instructions.All(item => item.Offset != scriptOffset))
                throw new InvalidOperationException("Manual insertion is not on an instruction boundary.");
            int workerIndex = AiDocument.GetWorkerIndexForCodeOffset(Math.Min(scriptOffset, AiDocument.ScriptCodeLength - 1));
            AtelWorker worker = AiDocument.Workers.First(item => item.Index == workerIndex);
            int cursor = 0;
            while (cursor < inserted.Length)
            {
                byte opcode = inserted[cursor];
                int length = (opcode & 0x80) != 0 ? 3 : 1;
                if (cursor + length > inserted.Length)
                    throw new InvalidOperationException("Manual insertion ends inside an instruction.");
                if (opcode is 0x34 or 0x3C or 0x40 or 0x54 or 0xB0 or 0xB1 or 0xB2)
                    throw new InvalidOperationException("Manual insertion contains a terminating or unsupported jump instruction. Use structured control-flow tools.");
                if (opcode is 0xD5 or 0xD6 or 0xD7)
                {
                    ushort jumpIndex = (ushort)(inserted[cursor + 1] | inserted[cursor + 2] << 8);
                    if (jumpIndex >= worker.JumpCount)
                        throw new InvalidOperationException($"Inserted conditional jump j{jumpIndex:X2} is outside worker w{workerIndex:X2}'s jump table.");
                }
                cursor += length;
            }
        }

        private static (int Prefix, int Suffix) FindSingleContiguousDifference(byte[] longer, byte[] shorter)
        {
            int prefix = 0;
            while (prefix < shorter.Length && longer[prefix] == shorter[prefix]) prefix++;
            int suffix = 0;
            while (suffix < shorter.Length - prefix &&
                   longer[longer.Length - 1 - suffix] == shorter[shorter.Length - 1 - suffix]) suffix++;
            return (prefix, suffix);
        }

        public void RestoreUnvalidatedAiHex()
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = "Restored the complete Battle Logic statement from the last validated Battle Script state.";
        }

        public AtelInstruction ApplyInstructionOperand(int scriptOffset, string operandText)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            ushort operand = ParseOperandText(operandText);

            AtelInstruction edited = AiDocument.ReplaceInstructionOperand(scriptOffset, operand);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Changed {edited.OpcodeName} operand at script offset 0x{scriptOffset:X4} to 0x{edited.Operand:X4}.";
            return edited;
        }

        public void ChangeWorkerJumpDestination(int workerIndex, int jumpIndex, int destinationOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AiDocument.SetWorkerJumpDestination(workerIndex, jumpIndex, destinationOffset);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            int chunkOffset = checked(AiDocument.ScriptCodeOffset + destinationOffset);
            AiStatus = $"Changed worker w{workerIndex:X2} jump j{jumpIndex:X2} destination to script offset " +
                $"0x{destinationOffset:X4} (Battle Script offset 0x{chunkOffset:X}). Press Save to write this change to disk.";
        }

        public int AddWorkerJump(int workerIndex, int destinationOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int jumpIndex = AiDocument.AddWorkerJump(workerIndex, destinationOffset);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            int chunkOffset = checked(AiDocument.ScriptCodeOffset + destinationOffset);
            AiStatus = $"Added worker w{workerIndex:X2} jump j{jumpIndex:X2} targeting script offset " +
                $"0x{destinationOffset:X4} (Battle Script offset 0x{chunkOffset:X}). Press Save to write this change to disk.";
            return jumpIndex;
        }

        public int AddWorkerVariable(int workerIndex)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int variableIndex = AiDocument.AddWorkerVariable(workerIndex);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Added variable[0x{variableIndex:X4}] to worker w{workerIndex:X2}. " +
                "It is now available in variable operand lists. Press Save to write this change to disk.";
            return variableIndex;
        }

        internal static ushort ParseOperandText(string operandText)
        {
            if (string.IsNullOrWhiteSpace(operandText)) throw new InvalidOperationException("Enter an operand value.");
            string clean = operandText.Trim();
            int numberBase = 10;
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[2..];
                numberBase = 16;
            }
            else if (clean.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[..^1];
                numberBase = 16;
            }

            int parsed;
            try { parsed = Convert.ToInt32(clean, numberBase); }
            catch (Exception) { throw new InvalidOperationException("Operand must be a decimal value or hexadecimal such as 0x409A or 409Ah."); }
            if (parsed < 0 || parsed > ushort.MaxValue)
                throw new InvalidOperationException("Operand must be between 0 and 65535 (0x0000–0xFFFF).");

            return (ushort)parsed;
        }

        public void ApplyGroupedInstructions(IReadOnlyList<AtelInstructionReplacement> replacements, int statementOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AiDocument.ReplaceInstructions(replacements);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Applied {replacements.Count} grouped change(s) atomically at statement script offset 0x{statementOffset:X4}.";
        }

        public AtelStatement ApplyStatementReplacement(int statementOffset, byte[] replacement, int sourceOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AtelStatement edited = AiDocument.ReplaceStatementBytes(statementOffset, replacement);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Replaced statement at script offset 0x{statementOffset:X4} with the equal-sized statement copied from script offset 0x{sourceOffset:X4}.";
            return edited;
        }

        public AtelStatement InsertStatement(int insertionOffset, byte[] statementBytes, int sourceOffset,
            bool preserveFunctionEntryAtInsertion = false)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int oldLength = AiDocument.Bytes.Length;
            AtelStatement inserted = AiDocument.InsertStatementBytes(insertionOffset, statementBytes,
                preserveFunctionEntryAtInsertion);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            int growth = AiDocument.Bytes.Length - oldLength;
            string storage = growth == 0 ? "existing alignment padding was used" : $"the post-code ATEL region was relocated by {growth} byte(s)";
            AiStatus = $"Inserted {statementBytes.Length} byte(s) copied from statement at script offset 0x{sourceOffset:X4} at script offset 0x{insertionOffset:X4}; {storage}, and code offsets were rebuilt.";
            return inserted;
        }

        public AtelStatement InsertStatementRange(int insertionOffset, byte[] rangeBytes, int sourceOffset,
            int workerIndex, IReadOnlyList<AtelRangeBranch> internalBranches,
            IReadOnlyList<AtelRangeFloat>? floatReferences = null,
            bool preserveFunctionEntryAtInsertion = false,
            bool preserveUnresolvedFloatIndices = false,
            bool preserveUnresolvedBranchIndices = false)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int oldLength = AiDocument.Bytes.Length;
            AtelStatement inserted = AiDocument.InsertStatementRangeBytes(insertionOffset, rangeBytes,
                workerIndex, internalBranches, floatReferences, preserveFunctionEntryAtInsertion,
                preserveUnresolvedFloatIndices, preserveUnresolvedBranchIndices);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            int growth = AiDocument.Bytes.Length - oldLength;
            string storage = growth == 0 ? "existing alignment padding was used" :
                $"the post-code ATEL region grew by {growth} byte(s)";
            AiStatus = $"Inserted {rangeBytes.Length} byte(s) copied from Battle Logic range at script offset " +
                $"0x{sourceOffset:X4} at script offset 0x{insertionOffset:X4}; {internalBranches.Count} internal " +
                $"branch(es) and {floatReferences?.Count ?? 0} float reference(s) were remapped, {storage}, and code offsets were rebuilt.";
            return inserted;
        }

        public AtelStatement ReplaceStatementRange(int replacementStart, int replacementEnd,
            byte[] rangeBytes, int sourceOffset, int workerIndex,
            IReadOnlyList<AtelRangeBranch> internalBranches,
            IReadOnlyList<AtelRangeFloat>? floatReferences = null,
            bool preserveUnresolvedFloatIndices = false,
            bool allowUnsafeDestinationEntries = false,
            bool preserveUnresolvedBranchIndices = false)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int oldLength = AiDocument.Bytes.Length;
            int removedLength = replacementEnd - replacementStart;
            AtelStatement replaced = AiDocument.ReplaceStatementRangeBytes(replacementStart,
                replacementEnd, rangeBytes, workerIndex, internalBranches, floatReferences,
                preserveUnresolvedFloatIndices, allowUnsafeDestinationEntries,
                preserveUnresolvedBranchIndices);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            int growth = AiDocument.Bytes.Length - oldLength;
            string storage = growth == 0 ? "the existing ATEL allocation was retained" :
                $"the post-code ATEL region grew by {growth} byte(s)";
            AiStatus = $"Replaced {removedLength} byte(s) of Battle Logic at script offset " +
                $"0x{replacementStart:X4} with {rangeBytes.Length} byte(s) copied from script offset " +
                $"0x{sourceOffset:X4}; {internalBranches.Count} internal branch(es) and " +
                $"{floatReferences?.Count ?? 0} float reference(s) were remapped, " +
                $"{storage}, and code offsets were rebuilt.";
            return replaced;
        }

        public (byte[] Bytes, string Preview, int FunctionStart) PreviewManualCodeBeforeReturn(
            int workerIndex, int functionIndex, string hexText)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AtelWorker worker = AiDocument.Workers.FirstOrDefault(item => item.Index == workerIndex)
                ?? throw new InvalidOperationException($"Worker w{workerIndex:X2} does not exist.");
            if (functionIndex < 0 || functionIndex >= worker.FunctionOffsets.Count)
                throw new InvalidOperationException("Select a specific function first.");
            int functionStart = worker.FunctionOffsets[functionIndex];
            int functionEnd = AiDocument.Workers.SelectMany(item => item.FunctionOffsets)
                .Where(offset => offset > functionStart).DefaultIfEmpty(AiDocument.ScriptCodeLength).Min();
            AtelInstruction? onlyInstruction = AiDocument.Instructions.FirstOrDefault(item => item.Offset == functionStart);
            if (functionEnd != functionStart + 1 || onlyInstruction?.Opcode != 0x3C)
                throw new InvalidOperationException("Manual insertion is available only for a function consisting of a single RETURN (3C).");

            byte[] bytes = AtelScriptDocument.ParseHexEditorText(hexText);
            if (bytes.Length == 0) throw new InvalidOperationException("Enter one or more instruction bytes.");
            ValidateManualInsertedInstructions(functionStart, bytes);
            var preview = new List<string>();
            for (int cursor = 0; cursor < bytes.Length;)
            {
                int length = (bytes[cursor] & 0x80) != 0 ? 3 : 1;
                byte[] instructionBytes = bytes.AsSpan(cursor, length).ToArray();
                var instruction = new AtelInstruction(functionStart + cursor, bytes[cursor], instructionBytes);
                preview.Add(instruction.CompactDisplay);
                cursor += length;
            }
            return (bytes, string.Join(Environment.NewLine, preview), functionStart);
        }

        public AtelStatement InsertManualCodeBeforeReturn(int workerIndex, int functionIndex, string hexText)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            (byte[] bytes, _, int functionStart) = PreviewManualCodeBeforeReturn(workerIndex, functionIndex, hexText);
            AtelStatement inserted = AiDocument.InsertStatementBytes(functionStart, bytes,
                preserveFunctionEntryAtInsertion: true);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Inserted {bytes.Length} manual code byte(s) before RETURN in w{workerIndex:X2}:f{functionIndex:X2}. " +
                "The function entry was preserved. Press Save to write this change to disk.";
            return inserted;
        }

        public int DeleteStatement(int statementOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AtelStatement statement = AiDocument.Statements.FirstOrDefault(item => item.Offset == statementOffset)
                ?? throw new InvalidOperationException($"No statement starts at script offset 0x{statementOffset:X4}.");
            bool preservedReturn = statement.Instructions.Any(item => item.Opcode == 0x3C);
            int removedLength = AiDocument.DeleteStatement(statementOffset);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = preservedReturn
                ? $"Deleted {removedLength} editable byte(s) at script offset 0x{statementOffset:X4} and preserved RETURN (3C); later code offsets were rebuilt. Press Save to write this change to disk."
                : $"Deleted the {removedLength}-byte statement at script offset 0x{statementOffset:X4}; jump-table destinations were retained and later code offsets were rebuilt. Press Save to write this change to disk.";
            return removedLength;
        }

        public AtelInstruction ApplyStructuredOperand(int scriptOffset, byte opcode, ushort operand, string description)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AtelInstruction edited = AiDocument.ReplaceInstruction(scriptOffset, opcode, operand);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Changed instruction at script offset 0x{scriptOffset:X4} to {description} 0x{operand:X4}.";
            return edited;
        }

        public AtelInstruction ApplyStructuredOpcode(int scriptOffset, byte opcode, string description)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            AtelInstruction edited = AiDocument.ReplaceOpcode(scriptOffset, opcode);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Changed instruction at script offset 0x{scriptOffset:X4} to {description}.";
            return edited;
        }

        public AtelInstruction ApplyFloatConstant(int scriptOffset, ushort floatIndex, string valueText)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            if (!float.TryParse(valueText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value) &&
                !float.TryParse(valueText, out value))
                throw new InvalidOperationException("Float value must be a number such as 2.5, 4, or -0.25.");
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Float value must be finite.");

            int references = AiDocument.GetFloatReferenceCount(floatIndex);
            AiDocument.ReplaceFloatConstant(floatIndex, value);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Changed shared float 0x{floatIndex:X4} to {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}; all {references} referencing instruction(s) were updated.";
            return AiDocument.Instructions.First(i => i.Offset == scriptOffset);
        }

        public void ReplaceAiHex()
        {
            byte[] search = AtelScriptDocument.ParseHexEditorText(AiSearchHex);
            byte[] replacement = AtelScriptDocument.ParseHexEditorText(AiReplacementHex);
            if (search.Length == 0) throw new InvalidOperationException("Enter at least one search byte.");
            if (replacement.Length != search.Length)
                throw new InvalidOperationException("Search and replacement must contain the same number of bytes.");

            byte[] bytes = AtelScriptDocument.ParseHexEditorText(AiHex);
            int replacements = 0;
            var replacedOffsets = new List<int>();
            for (int i = 0; i <= bytes.Length - search.Length; i++)
            {
                if (!bytes.AsSpan(i, search.Length).SequenceEqual(search)) continue;
                replacedOffsets.Add(i);
                replacement.CopyTo(bytes, i);
                replacements++;
                i += search.Length - 1;
            }
            if (replacements == 0) throw new InvalidOperationException("The search sequence was not found in this Battle Script.");

            AiHex = FormatHex(bytes);
            ApplyAiHex();
            AiSearchOffsets = replacedOffsets;
            AiSearchLength = replacement.Length;
            AiStatus = $"Replaced {replacements} occurrence(s) and validated the Battle Script.";
        }

        public void FindAiHex()
        {
            byte[] search = AtelScriptDocument.ParseHexEditorText(AiSearchHex);
            if (search.Length == 0) throw new InvalidOperationException("Enter at least one search byte.");

            byte[] bytes = AtelScriptDocument.ParseHexEditorText(AiHex);
            var offsets = new List<int>();
            for (int i = 0; i <= bytes.Length - search.Length; i++)
            {
                if (!bytes.AsSpan(i, search.Length).SequenceEqual(search)) continue;
                offsets.Add(i);
                i += search.Length - 1;
            }
            if (offsets.Count == 0)
                throw new InvalidOperationException($"Sequence {Convert.ToHexString(search)} was not found in this Battle Script.");

            AiSearchOffsets = offsets;
            AiSearchLength = search.Length;
            string shownOffsets = string.Join(", ", offsets.Take(32).Select(o => $"0x{o:X}"));
            string suffix = offsets.Count > 32 ? $" (+{offsets.Count - 32} more)" : "";
            AiStatus = $"Found {offsets.Count} match(es) at Battle Script offset(s): {shownOffsets}{suffix}";
        }

        private static string FormatHex(byte[] bytes)
        {
            AtelScriptDocument temporary = AtelScriptDocument.Read(bytes);
            return temporary.ToHexEditorText();
        }

        public void Save()
        {
            ValidateEditorValues();
            ApplyAiHex();
            Monster_StatSheet editedSheet = MonsterStatSheet.Unwrap();
            string kernelPath = Project_Service.Instance.GetPathKernelMonsterUs(_monsterId);
            byte[]? rebuiltKernel = null;
            Monster_StatSheet? kernelSheet = null;
            if (_usesLocalizedMonsterText && File.Exists(kernelPath))
            {
                Monster_KernelFile kernelFile = Monster_KernelFile.Read(File.ReadAllBytes(kernelPath));
                kernelSheet = kernelFile.GetGlobalEntry(_monsterId);
                CopyLocalizedText(editedSheet, kernelSheet);
                rebuiltKernel = kernelFile.Write();

                // mXXX.bin is the Japanese gameplay package. Update its stats,
                // but retain its local Japanese scripts when the localized
                // kernel text is available.
                CopyText(MonsterFile.StatSheetFile, editedSheet);
            }
            else
            {
                // A project without the localized split remains fully usable:
                // the displayed Japanese text came from mXXX.bin and is saved
                // back into that same self-contained file.
                _usesLocalizedMonsterText = false;
            }
            MonsterFile.StatSheetFile = editedSheet;
            MonsterFile.LootFile = MonsterLoot.Unwrap();

            byte[] rebuilt = MonsterFile.Write();
            Monster_File roundTrip = Monster_File.Read(rebuilt);
            AtelScriptDocument.Read(roundTrip.AiFile, roundTrip.WorkerFile);
            if (rebuiltKernel != null && kernelSheet != null)
            {
                Monster_KernelFile kernelRoundTrip = Monster_KernelFile.Read(rebuiltKernel);
                Monster_StatSheet verifiedText = kernelRoundTrip.GetGlobalEntry(_monsterId);
                if (!verifiedText.NameScriptBytes.SequenceEqual(kernelSheet.NameScriptBytes) ||
                    !verifiedText.SensorScriptBytes.SequenceEqual(kernelSheet.SensorScriptBytes) ||
                    !verifiedText.ScanScriptBytes.SequenceEqual(kernelSheet.ScanScriptBytes))
                    throw new InvalidDataException("Localized monster text failed round-trip verification.");
            }

            File.WriteAllBytes(MonsterPath, rebuilt);
            if (rebuiltKernel != null)
            {
                File.WriteAllBytes(kernelPath, rebuiltKernel);
                AiStatus = EditorSaveStatus.Success("Monster");
            }
            else
            {
                AiStatus = EditorSaveStatus.Success("Monster");
            }
        }

        private void ValidateEditorValues()
        {
            if (MonsterStatSheet.PoisonDamage > 100)
                throw new InvalidDataException("Poison Damage must be between 0 and 100.");
            byte[] quantities =
            [
                MonsterLoot.Drop1Count, MonsterLoot.Drop1RareCount,
                MonsterLoot.Drop2Count, MonsterLoot.Drop2RareCount,
                MonsterLoot.DropOverkillCount, MonsterLoot.DropOverkillRareCount,
                MonsterLoot.DropOverkill2Count, MonsterLoot.DropOverkill2RareCount,
                MonsterLoot.StealCount, MonsterLoot.StealRareCount, MonsterLoot.BribeCount
            ];
            if (quantities.Any(value => value > 99))
                throw new InvalidDataException("Monster loot quantities must be between 0 and 99.");

            IEnumerable<GameIndex_Wrapper> DirectIndexes()
            {
                yield return MonsterStatSheet.ForcedAbility;
                yield return MonsterStatSheet.Ability1; yield return MonsterStatSheet.Ability2;
                yield return MonsterStatSheet.Ability3; yield return MonsterStatSheet.Ability4;
                yield return MonsterStatSheet.Ability5; yield return MonsterStatSheet.Ability6;
                yield return MonsterStatSheet.Ability7; yield return MonsterStatSheet.Ability8;
                yield return MonsterStatSheet.Ability9; yield return MonsterStatSheet.Ability10;
                yield return MonsterStatSheet.Ability11; yield return MonsterStatSheet.Ability12;
                yield return MonsterStatSheet.Ability13; yield return MonsterStatSheet.Ability14;
                yield return MonsterStatSheet.Ability15; yield return MonsterStatSheet.Ability16;
                yield return MonsterLoot.Drop1; yield return MonsterLoot.Drop1Rare;
                yield return MonsterLoot.Drop2; yield return MonsterLoot.Drop2Rare;
                yield return MonsterLoot.DropOverkill1; yield return MonsterLoot.DropOverkill1Rare;
                yield return MonsterLoot.DropOverkill2; yield return MonsterLoot.DropOverkill2Rare;
                yield return MonsterLoot.Steal; yield return MonsterLoot.StealRare;
                yield return MonsterLoot.Bribe;
            }

            IEnumerable<GameIndex_Wrapper> indexes = DirectIndexes()
                .Concat(MonsterLoot.TidusWeapons).Concat(MonsterLoot.TidusArmors)
                .Concat(MonsterLoot.YunaWeapons).Concat(MonsterLoot.YunaArmors)
                .Concat(MonsterLoot.AuronWeapons).Concat(MonsterLoot.AuronArmors)
                .Concat(MonsterLoot.KimahriWeapons).Concat(MonsterLoot.KimahriArmors)
                .Concat(MonsterLoot.WakkaWeapons).Concat(MonsterLoot.WakkaArmors)
                .Concat(MonsterLoot.LuluWeapons).Concat(MonsterLoot.LuluArmors)
                .Concat(MonsterLoot.RikkuWeapons).Concat(MonsterLoot.RikkuArmors);
            if (indexes.Any(value => value == null || value.Index > 0xFFF))
                throw new InvalidDataException("Ability and item indexes must be between 0 and 4095.");

            long textPoolSize =
                (MonsterStatSheet.NameScriptBytes?.Length ?? 0) + 1L +
                (MonsterStatSheet.SensorScriptBytes?.Length ?? 0) + 1L +
                (MonsterStatSheet.UnusedText1ScriptBytes?.Length ?? 0) + 1L +
                (MonsterStatSheet.ScanScriptBytes?.Length ?? 0) + 1L +
                (MonsterStatSheet.UnusedText2ScriptBytes?.Length ?? 0) + 1L;
            if (textPoolSize > ushort.MaxValue + 1L)
                throw new InvalidDataException(
                    "Monster Name, Sensor, and Scan text exceed the 16-bit text-pool limit.");
        }

        private bool LoadLocalizedTextIntoWrapper()
        {
            string path = Project_Service.Instance.GetPathKernelMonsterUs(_monsterId);
            if (!File.Exists(path))
            {
                MonsterStatSheet.UseEnglishText = false;
                return false;
            }

            Monster_KernelFile kernel = Monster_KernelFile.Read(File.ReadAllBytes(path));
            Monster_StatSheet localized = kernel.GetGlobalEntry(_monsterId);
            MonsterStatSheet.UseEnglishText = true;
            MonsterStatSheet.NameScriptBytes = (byte[])localized.NameScriptBytes.Clone();
            MonsterStatSheet.SensorScriptBytes = (byte[])localized.SensorScriptBytes.Clone();
            MonsterStatSheet.ScanScriptBytes = (byte[])localized.ScanScriptBytes.Clone();
            MonsterStatSheet.NameScriptId = localized.NameScriptId;
            MonsterStatSheet.SensorScriptId = localized.SensorScriptId;
            MonsterStatSheet.ScanScriptId = localized.ScanScriptId;
            return true;
        }

        private static void CopyText(Monster_StatSheet source, Monster_StatSheet destination)
        {
            destination.NameScriptBytes = (byte[])source.NameScriptBytes.Clone();
            destination.SensorScriptBytes = (byte[])source.SensorScriptBytes.Clone();
            destination.UnusedText1ScriptBytes = (byte[])source.UnusedText1ScriptBytes.Clone();
            destination.ScanScriptBytes = (byte[])source.ScanScriptBytes.Clone();
            destination.UnusedText2ScriptBytes = (byte[])source.UnusedText2ScriptBytes.Clone();
            destination.NameScriptId = source.NameScriptId;
            destination.SensorScriptId = source.SensorScriptId;
            destination.UnusedText1ScriptId = source.UnusedText1ScriptId;
            destination.ScanScriptId = source.ScanScriptId;
            destination.UnusedText2ScriptId = source.UnusedText2ScriptId;
        }

        private static void CopyLocalizedText(Monster_StatSheet source, Monster_StatSheet destination)
        {
            destination.NameScriptBytes = (byte[])source.NameScriptBytes.Clone();
            destination.SensorScriptBytes = (byte[])source.SensorScriptBytes.Clone();
            destination.ScanScriptBytes = (byte[])source.ScanScriptBytes.Clone();
            destination.NameScriptId = source.NameScriptId;
            destination.SensorScriptId = source.SensorScriptId;
            destination.ScanScriptId = source.ScanScriptId;
        }

        private static int ParseMonsterId(string monsterPath)
        {
            string name = Path.GetFileNameWithoutExtension(monsterPath);
            if (name.Length == 4 && name[0] == 'm' && int.TryParse(name.AsSpan(1), out int id))
                return id;
            throw new InvalidDataException($"Cannot determine monster ID from path: {monsterPath}");
        }
    }
}
