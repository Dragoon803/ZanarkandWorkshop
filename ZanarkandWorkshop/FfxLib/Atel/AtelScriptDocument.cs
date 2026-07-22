using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Globalization;

namespace FFXProjectEditor.FfxLib.Atel;

public sealed class AtelScriptDocument
{
    public const int StaticHeaderLength = 0x38;
    public const int WorkerHeaderLength = 0x34;

    public byte[] Bytes { get; private set; }
    public int ScriptCodeLength { get; private set; }
    public int ScriptCodeOffset { get; }
    public int WorkerCount { get; }
    public int ActorCount { get; }
    public bool RecoveredMissingCodeLength { get; }
    public string Creator { get; }
    public string ScriptId { get; }
    public IReadOnlyList<AtelWorker> Workers { get; private set; }
    public IReadOnlyList<AtelInstruction> Instructions { get; private set; }
    public IReadOnlyList<AtelStatement> Statements { get; private set; } = [];
    private Func<ushort, string?>? CommandNameResolver { get; set; }
    private byte[]? BattleWorkerMappingBytes { get; }

    private AtelScriptDocument(byte[] bytes, byte[]? battleWorkerMappingBytes = null)
    {
        Bytes = bytes;
        BattleWorkerMappingBytes = battleWorkerMappingBytes == null ? null : (byte[])battleWorkerMappingBytes.Clone();
        RequireRange(bytes, 0, StaticHeaderLength, "ATEL header");
        int declaredScriptCodeLength = ReadInt32(bytes, 0x00);
        int creatorOffset = ReadInt32(bytes, 0x08);
        int scriptIdOffset = ReadInt32(bytes, 0x0C);
        ScriptCodeOffset = ReadInt32(bytes, 0x30);
        WorkerCount = ReadUInt16(bytes, 0x34);
        ActorCount = ReadUInt16(bytes, 0x36);

        if (declaredScriptCodeLength < 0)
            throw new InvalidOperationException("The ATEL script length is negative.");
        Creator = ReadNullTerminatedUtf8(bytes, creatorOffset);
        ScriptId = ReadNullTerminatedUtf8(bytes, scriptIdOffset);

        var workers = new List<AtelWorker>(WorkerCount);
        RequireRange(bytes, StaticHeaderLength, checked(WorkerCount * 4), "ATEL worker offset table");
        for (int i = 0; i < WorkerCount; i++)
        {
            int workerOffset = ReadInt32(bytes, StaticHeaderLength + i * 4);
            workers.Add(AtelWorker.Read(bytes, i, workerOffset));
        }
        ApplyBattleWorkerMetadata(workers, BattleWorkerMappingBytes);
        Workers = workers;
        if (declaredScriptCodeLength == 0 && ScriptCodeOffset > 0 && workers.Count > 0)
        {
            declaredScriptCodeLength = InferMissingScriptCodeLength(bytes, ScriptCodeOffset, workers);
            WriteInt32(bytes, 0x00, declaredScriptCodeLength);
            RecoveredMissingCodeLength = true;
        }
        ScriptCodeLength = declaredScriptCodeLength;
        RequireRange(bytes, ScriptCodeOffset, ScriptCodeLength, "ATEL script code");
        Instructions = ParseInstructions(bytes, ScriptCodeOffset, ScriptCodeLength);
        Statements = AtelDecompiler.Translate(Instructions, CommandNameResolver, ResolveFloatConstant);
    }

    public static AtelScriptDocument Read(byte[] bytes)
        => Read(bytes, null);

    public static AtelScriptDocument Read(byte[] bytes, byte[]? battleWorkerMappingBytes)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException("This monster has no ATEL AI chunk.");
        return new AtelScriptDocument((byte[])bytes.Clone(), battleWorkerMappingBytes);
    }

    public void ReplaceBytes(byte[] replacement)
    {
        if (replacement.Length != Bytes.Length)
            throw new InvalidOperationException($"Fixed-layout editing requires exactly {Bytes.Length} bytes; received {replacement.Length}.");

        // Parse first. This rejects corrupt headers, tables, and instruction extents before accepting the edit.
        AtelScriptDocument validated = Read(replacement, BattleWorkerMappingBytes);
        if (validated.ScriptCodeOffset != ScriptCodeOffset || validated.ScriptCodeLength != ScriptCodeLength || validated.WorkerCount != WorkerCount)
            throw new InvalidOperationException("This milestone does not permit structural ATEL header changes.");

        Bytes = (byte[])replacement.Clone();
        Workers = validated.Workers;
        Instructions = ParseInstructions(Bytes, ScriptCodeOffset, ScriptCodeLength);
        Statements = AtelDecompiler.Translate(Instructions, CommandNameResolver, ResolveFloatConstant);
    }

    private static void ApplyBattleWorkerMetadata(IReadOnlyList<AtelWorker> workers, byte[]? mapping)
    {
        // Monster worker metadata is stored in the separate chunk immediately after the ATEL chunk.
        // This follows FFX Data Parser's AtelScriptObject.parseBattleWorkerTypes layout.
        if (mapping == null || mapping.Length < 2) return;

        int mappedWorkerCount = mapping[0];
        int slotCount = mapping[1];
        if (mapping.Length < 2 + slotCount) return;

        var sectionToPurposeSlot = new Dictionary<int, int>();
        for (int slot = 0; slot < slotCount; slot++)
        {
            int section = mapping[slot + 2];
            if (section != 0xFF) sectionToPurposeSlot[section] = slot;
        }

        int sectionTableOffset = (slotCount + 0x03) & ~1;
        for (int section = 0; section < mappedWorkerCount; section++)
        {
            int offset = sectionTableOffset + section * 4;
            if (offset + 3 >= mapping.Length) break;

            int workerIndex = mapping[offset];
            if (workerIndex < 0 || workerIndex >= workers.Count) continue;

            AtelWorker worker = workers[workerIndex];
            worker.BattleWorkerType = mapping[offset + 1];
            worker.PurposeSlot = sectionToPurposeSlot.TryGetValue(section, out int purposeSlot)
                ? purposeSlot
                : null;

            int sectionOffset = mapping[offset + 2] | (mapping[offset + 3] << 8);
            if (sectionOffset < 0 || sectionOffset + 1 >= mapping.Length) continue;
            int tagCount = mapping[sectionOffset] | (mapping[sectionOffset + 1] << 8);
            int payloadOffset = sectionOffset + 2;
            for (int slot = 0; slot < tagCount; slot++)
            {
                int entryOffset = payloadOffset + slot * 2;
                if (entryOffset + 1 >= mapping.Length) break;
                int functionIndex = mapping[entryOffset] | (mapping[entryOffset + 1] << 8);
                if (functionIndex != 0xFFFF && functionIndex < worker.FunctionCount)
                    worker.FunctionBattleSlots[functionIndex] = slot;
            }
        }
    }

    public void SetCommandNameResolver(Func<ushort, string?> resolver)
    {
        CommandNameResolver = resolver;
        Statements = AtelDecompiler.Translate(Instructions, CommandNameResolver, ResolveFloatConstant);
    }

    private string? ResolveFloatConstant(ushort index)
    {
        AtelWorker? worker = Workers.FirstOrDefault(w => index < w.FloatConstantBits.Count);
        if (worker == null) return null;
        int bits = worker.FloatConstantBits[index];
        float value = BitConverter.Int32BitsToSingle(bits);
        return $"{value.ToString("0.0#####", CultureInfo.InvariantCulture)} [0x{unchecked((uint)bits):X8}]";
    }

    public bool TryGetFloatConstant(ushort index, out float value)
    {
        AtelWorker? worker = Workers.FirstOrDefault(w => index < w.FloatConstantBits.Count);
        if (worker == null) { value = 0; return false; }
        value = BitConverter.Int32BitsToSingle(worker.FloatConstantBits[index]);
        return true;
    }

    public int GetFloatReferenceCount(ushort index) => Instructions.Count(i => i.Opcode == 0xAF && i.Operand == index);

    public bool TryFindUnusedFloatConstant(out ushort index)
    {
        int count = Workers.Count == 0 ? 0 : Workers.Min(w => w.FloatConstantBits.Count);
        HashSet<ushort> used = Instructions.Where(i => i.Opcode == 0xAF).Select(i => i.Operand).ToHashSet();
        for (int candidate = 0; candidate < count; candidate++)
        {
            if (!used.Contains((ushort)candidate)) { index = (ushort)candidate; return true; }
        }
        index = 0;
        return false;
    }

    public bool TryFindFloatConstant(float value, ushort excludedIndex, out ushort index)
    {
        int wantedBits = BitConverter.SingleToInt32Bits(value);
        AtelWorker? worker = Workers.FirstOrDefault();
        if (worker != null)
        {
            for (int candidate = 0; candidate < worker.FloatConstantBits.Count; candidate++)
            {
                if (candidate != excludedIndex && worker.FloatConstantBits[candidate] == wantedBits)
                {
                    index = (ushort)candidate;
                    return true;
                }
            }
        }
        index = 0;
        return false;
    }

    public void ReplaceFloatConstant(ushort index, float value)
    {
        AtelWorker[] owners = Workers.Where(w => index < w.FloatConstantBits.Count).ToArray();
        if (owners.Length == 0) throw new InvalidOperationException($"Float reference 0x{index:X4} is outside this AI chunk's float table.");
        byte[] edited = (byte[])Bytes.Clone();
        byte[] valueBytes = BitConverter.GetBytes(value);
        foreach (int tableOffset in owners.Select(w => w.FloatConstantOffset).Distinct())
        {
            int valueOffset = checked(tableOffset + index * 4);
            RequireRange(edited, valueOffset, 4, $"float constant 0x{index:X4}");
            Array.Copy(valueBytes, 0, edited, valueOffset, 4);
        }
        ReplaceBytes(edited);
    }

    public AtelInstruction ReplaceInstructionOperand(int scriptOffset, ushort operand)
    {
        AtelInstruction instruction = Instructions.FirstOrDefault(i => i.Offset == scriptOffset)
            ?? throw new InvalidOperationException($"No instruction starts at script offset 0x{scriptOffset:X4}.");
        if (!instruction.HasOperand)
            throw new InvalidOperationException($"{instruction.OpcodeName} at 0x{scriptOffset:X4} is a one-byte instruction and has no editable operand.");

        byte[] edited = (byte[])Bytes.Clone();
        int fileOffset = checked(ScriptCodeOffset + scriptOffset);
        edited[fileOffset + 1] = (byte)(operand & 0xFF);
        edited[fileOffset + 2] = (byte)(operand >> 8);
        ReplaceBytes(edited);
        return Instructions.First(i => i.Offset == scriptOffset);
    }

    public byte[] GetStatementBytes(int statementOffset)
    {
        AtelStatement statement = Statements.FirstOrDefault(item => item.Offset == statementOffset)
            ?? throw new InvalidOperationException($"No statement starts at script offset 0x{statementOffset:X4}.");
        byte[] result = new byte[statement.ByteLength];
        Array.Copy(Bytes, checked(ScriptCodeOffset + statement.Offset), result, 0, result.Length);
        return result;
    }

    public int GetWorkerIndexForCodeOffset(int scriptOffset)
    {
        AtelWorker? owner = Workers
            .Where(worker => worker.FunctionOffsets.Count > 0 && worker.FunctionOffsets.Min() <= scriptOffset)
            .OrderByDescending(worker => worker.FunctionOffsets.Min())
            .FirstOrDefault();
        if (owner == null)
            throw new InvalidOperationException($"Script offset 0x{scriptOffset:X4} is not inside a known worker function range.");
        return owner.Index;
    }

    public AtelStatement ReplaceStatementBytes(int statementOffset, byte[] replacement)
    {
        AtelStatement statement = Statements.FirstOrDefault(item => item.Offset == statementOffset)
            ?? throw new InvalidOperationException($"No statement starts at script offset 0x{statementOffset:X4}.");
        if (replacement.Length != statement.ByteLength)
            throw new InvalidOperationException(
                $"Copied statement is {replacement.Length} byte(s), but the destination is {statement.ByteLength} byte(s). " +
                $"Paste requires equal byte lengths. Select a {replacement.Length}-byte destination or copy a {statement.ByteLength}-byte statement.");

        byte[] edited = (byte[])Bytes.Clone();
        Array.Copy(replacement, 0, edited, checked(ScriptCodeOffset + statement.Offset), replacement.Length);
        ReplaceBytes(edited);
        return Statements.First(item => item.Offset == statementOffset);
    }

    public AtelStatement InsertStatementBytes(int insertionOffset, byte[] insertedBytes)
    {
        if (insertedBytes == null || insertedBytes.Length == 0)
            throw new InvalidOperationException("The copied statement is empty.");
        if (insertionOffset < 0 || insertionOffset > ScriptCodeLength ||
            (insertionOffset < ScriptCodeLength && Instructions.All(item => item.Offset != insertionOffset)))
            throw new InvalidOperationException($"Insertion offset 0x{insertionOffset:X4} is not an instruction boundary.");

        int oldCodeEnd = checked(ScriptCodeOffset + ScriptCodeLength);
        int nextDataOffset = Workers.Select(worker => worker.SharedDataOffset)
            .Where(offset => offset >= oldCodeEnd).DefaultIfEmpty(Bytes.Length).Min();
        int availablePadding = nextDataOffset - oldCodeEnd;
        if (Bytes.Skip(oldCodeEnd).Take(availablePadding).Any(value => value != 0))
            throw new InvalidOperationException("The bytes after the script are not empty padding, so this insertion was refused.");

        int relocationAmount = insertedBytes.Length <= availablePadding
            ? 0
            : checked((insertedBytes.Length - availablePadding + 0x0F) & ~0x0F);
        int relocatedDataOffset = checked(nextDataOffset + relocationAmount);
        byte[] edited = new byte[checked(Bytes.Length + relocationAmount)];
        int insertionFileOffset = checked(ScriptCodeOffset + insertionOffset);
        Array.Copy(Bytes, 0, edited, 0, insertionFileOffset);
        Array.Copy(insertedBytes, 0, edited, insertionFileOffset, insertedBytes.Length);
        Array.Copy(Bytes, insertionFileOffset, edited, insertionFileOffset + insertedBytes.Length,
            ScriptCodeLength - insertionOffset);
        Array.Copy(Bytes, nextDataOffset, edited, relocatedDataOffset, Bytes.Length - nextDataOffset);
        WriteInt32(edited, 0x00, checked(ScriptCodeLength + insertedBytes.Length));

        if (relocationAmount > 0)
            RelocateAbsolutePointers(edited, nextDataOffset, relocationAmount);

        foreach (AtelWorker worker in Workers)
        {
            ShiftOffsetTable(edited, RelocatedOffset(worker.FunctionTableOffset, nextDataOffset, relocationAmount),
                worker.FunctionOffsets, insertionOffset, insertedBytes.Length);
            ShiftOffsetTable(edited, RelocatedOffset(worker.JumpTableOffset, nextDataOffset, relocationAmount),
                worker.JumpOffsets, insertionOffset, insertedBytes.Length);
        }

        AtelScriptDocument validated = Read(edited);
        if (validated.ScriptCodeOffset != ScriptCodeOffset || validated.WorkerCount != WorkerCount ||
            validated.ScriptCodeLength != ScriptCodeLength + insertedBytes.Length)
            throw new InvalidOperationException("The rebuilt ATEL structure did not preserve its required header layout.");
        Bytes = validated.Bytes;
        ScriptCodeLength = validated.ScriptCodeLength;
        Workers = validated.Workers;
        Instructions = validated.Instructions;
        Statements = AtelDecompiler.Translate(Instructions, CommandNameResolver, ResolveFloatConstant);
        return Statements.FirstOrDefault(item => item.Offset == insertionOffset)
            ?? throw new InvalidOperationException("The inserted bytes did not decode as a complete statement.");
    }

    public int DeleteStatement(int statementOffset)
    {
        AtelStatement statement = Statements.FirstOrDefault(item => item.Offset == statementOffset)
            ?? throw new InvalidOperationException($"No statement starts at script offset 0x{statementOffset:X4}.");
        AtelInstruction? protectedInstruction = statement.Instructions.FirstOrDefault(instruction => instruction.Opcode is
            0x34 or 0x3C or 0x40 or 0x54 or 0xB0 or 0xB1 or 0xB2);
        if (protectedInstruction?.Opcode == 0x3C)
            throw new InvalidOperationException("This statement contains RETURN, which ends the current function. Deleting it could let execution continue into unrelated AI logic, so it is protected.");
        if (protectedInstruction?.Opcode == 0xB0)
            throw new InvalidOperationException("This statement contains JUMP, which controls where execution continues. Deleting it would change the script's control flow, so it is protected.");
        if (protectedInstruction != null)
            throw new InvalidOperationException($"This statement contains {protectedInstruction.OpcodeName}, which terminates control flow or uses a jump form that cannot be safely rebuilt yet, so it is protected.");

        int deletionEnd = checked(statement.Offset + statement.ByteLength);
        foreach (AtelWorker worker in Workers)
        {
            if (worker.FunctionOffsets.Any(offset => offset >= statement.Offset && offset < deletionEnd))
                throw new InvalidOperationException("This statement is a function entry point and cannot be deleted safely.");
            if (worker.JumpOffsets.Any(offset => offset >= statement.Offset && offset < deletionEnd))
                throw new InvalidOperationException("This statement is a jump destination and cannot be deleted safely.");
        }

        byte[] edited = (byte[])Bytes.Clone();
        int deletionFileOffset = checked(ScriptCodeOffset + statement.Offset);
        int oldCodeEnd = checked(ScriptCodeOffset + ScriptCodeLength);
        int trailingCodeLength = checked(ScriptCodeLength - deletionEnd);
        Array.Copy(edited, deletionFileOffset + statement.ByteLength, edited, deletionFileOffset, trailingCodeLength);
        Array.Clear(edited, oldCodeEnd - statement.ByteLength, statement.ByteLength);
        WriteInt32(edited, 0x00, checked(ScriptCodeLength - statement.ByteLength));

        foreach (AtelWorker worker in Workers)
        {
            ShiftOffsetTableForDeletion(edited, worker.FunctionTableOffset, worker.FunctionOffsets,
                deletionEnd, statement.ByteLength);
            ShiftOffsetTableForDeletion(edited, worker.JumpTableOffset, worker.JumpOffsets,
                deletionEnd, statement.ByteLength);
        }

        AtelScriptDocument validated = Read(edited);
        if (validated.ScriptCodeOffset != ScriptCodeOffset || validated.WorkerCount != WorkerCount ||
            validated.ScriptCodeLength != ScriptCodeLength - statement.ByteLength)
            throw new InvalidOperationException("The rebuilt ATEL structure did not preserve its required header layout.");
        Bytes = validated.Bytes;
        ScriptCodeLength = validated.ScriptCodeLength;
        Workers = validated.Workers;
        Instructions = validated.Instructions;
        Statements = AtelDecompiler.Translate(Instructions, CommandNameResolver, ResolveFloatConstant);
        return statement.ByteLength;
    }

    private void RelocateAbsolutePointers(byte[] bytes, int oldDataOffset, int amount)
    {
        int oldMetaHeaderOffset = ReadInt32(Bytes, 0x2C);
        foreach (int field in new[] { 0x04, 0x08, 0x0C, 0x20, 0x24, 0x28, 0x2C, 0x30 })
            RelocatePointerField(bytes, field, oldDataOffset, amount);
        WriteInt32(bytes, 0x10, checked(ReadInt32(Bytes, 0x10) + amount));

        foreach (AtelWorker worker in Workers)
        {
            foreach (int field in new[] { 0x14, 0x18, 0x1C, 0x20, 0x24, 0x2C, 0x30 })
                RelocatePointerField(bytes, worker.HeaderOffset + field, oldDataOffset, amount);
        }

        if (oldMetaHeaderOffset >= oldDataOffset)
        {
            int newMetaHeaderOffset = checked(oldMetaHeaderOffset + amount);
            RequireRange(bytes, newMetaHeaderOffset, 0x40, "relocated ATEL metadata header");
            int oldMapPointerHeader = ReadInt32(bytes, newMetaHeaderOffset + 0x3C);
            foreach (int field in new[] { 0x0C, 0x10, 0x18, 0x1C, 0x24, 0x28, 0x2C, 0x3C })
                RelocatePointerField(bytes, newMetaHeaderOffset + field, oldDataOffset, amount);
            if (oldMapPointerHeader >= oldDataOffset)
            {
                int newMapPointerHeader = checked(oldMapPointerHeader + amount);
                RequireRange(bytes, newMapPointerHeader, 0x10, "relocated ATEL metadata map-pointer header");
                RelocatePointerField(bytes, newMapPointerHeader + 0x08, oldDataOffset, amount);
                RelocatePointerField(bytes, newMapPointerHeader + 0x0C, oldDataOffset, amount);
            }
        }
    }

    private static int RelocatedOffset(int offset, int oldDataOffset, int amount) =>
        offset >= oldDataOffset ? checked(offset + amount) : offset;

    private static void RelocatePointerField(byte[] bytes, int fieldOffset, int oldDataOffset, int amount)
    {
        RequireRange(bytes, fieldOffset, 4, "ATEL pointer field");
        int value = ReadInt32(bytes, fieldOffset);
        if (value >= oldDataOffset)
            WriteInt32(bytes, fieldOffset, checked(value + amount));
    }

    private static void ShiftOffsetTable(byte[] bytes, int tableOffset, IReadOnlyList<int> offsets,
        int insertionOffset, int amount)
    {
        if (offsets.Count == 0) return;
        RequireRange(bytes, tableOffset, checked(offsets.Count * 4), "ATEL code-offset table");
        for (int index = 0; index < offsets.Count; index++)
        {
            int value = offsets[index];
            if (value >= insertionOffset)
                WriteInt32(bytes, tableOffset + index * 4, checked(value + amount));
        }
    }

    private static void ShiftOffsetTableForDeletion(byte[] bytes, int tableOffset, IReadOnlyList<int> offsets,
        int deletionEnd, int amount)
    {
        if (offsets.Count == 0) return;
        RequireRange(bytes, tableOffset, checked(offsets.Count * 4), "ATEL code-offset table");
        for (int index = 0; index < offsets.Count; index++)
        {
            int value = offsets[index];
            if (value >= deletionEnd)
                WriteInt32(bytes, tableOffset + index * 4, checked(value - amount));
        }
    }

    public AtelInstruction ReplaceInstruction(int scriptOffset, byte opcode, ushort operand)
    {
        AtelInstruction instruction = Instructions.FirstOrDefault(i => i.Offset == scriptOffset)
            ?? throw new InvalidOperationException($"No instruction starts at script offset 0x{scriptOffset:X4}.");
        if (instruction.Bytes.Length != 3)
            throw new InvalidOperationException("Structured replacement requires an existing three-byte instruction.");
        byte[] edited = (byte[])Bytes.Clone();
        int fileOffset = checked(ScriptCodeOffset + scriptOffset);
        edited[fileOffset] = opcode;
        edited[fileOffset + 1] = (byte)(operand & 0xFF);
        edited[fileOffset + 2] = (byte)(operand >> 8);
        ReplaceBytes(edited);
        return Instructions.First(i => i.Offset == scriptOffset);
    }

    public AtelInstruction ReplaceOpcode(int scriptOffset, byte opcode)
    {
        AtelInstruction instruction = Instructions.FirstOrDefault(i => i.Offset == scriptOffset)
            ?? throw new InvalidOperationException($"No instruction starts at script offset 0x{scriptOffset:X4}.");
        bool oldHasOperand = (instruction.Opcode & 0x80) != 0;
        bool newHasOperand = (opcode & 0x80) != 0;
        if (oldHasOperand != newHasOperand)
            throw new InvalidOperationException("The replacement opcode must have the same instruction length.");
        byte[] edited = (byte[])Bytes.Clone();
        edited[checked(ScriptCodeOffset + scriptOffset)] = opcode;
        ReplaceBytes(edited);
        return Instructions.First(i => i.Offset == scriptOffset);
    }

    public void ReplaceInstructions(IReadOnlyList<AtelInstructionReplacement> replacements)
    {
        byte[] edited = (byte[])Bytes.Clone();
        foreach (AtelInstructionReplacement replacement in replacements)
        {
            AtelInstruction instruction = Instructions.FirstOrDefault(i => i.Offset == replacement.ScriptOffset)
                ?? throw new InvalidOperationException($"No instruction starts at script offset 0x{replacement.ScriptOffset:X4}.");
            bool oldHasOperand = instruction.HasOperand;
            bool newHasOperand = (replacement.Opcode & 0x80) != 0;
            if (oldHasOperand != newHasOperand)
                throw new InvalidOperationException($"Replacement at 0x{replacement.ScriptOffset:X4} must retain its instruction length.");
            int fileOffset = checked(ScriptCodeOffset + replacement.ScriptOffset);
            edited[fileOffset] = replacement.Opcode;
            if (oldHasOperand)
            {
                edited[fileOffset + 1] = (byte)(replacement.Operand & 0xFF);
                edited[fileOffset + 2] = (byte)(replacement.Operand >> 8);
            }
        }
        ReplaceBytes(edited);
    }

    public string ToHexEditorText()
    {
        var result = new StringBuilder(Bytes.Length * 3);
        for (int offset = 0; offset < Bytes.Length; offset += 16)
        {
            int count = Math.Min(16, Bytes.Length - offset);
            result.Append(offset.ToString("X6")).Append(": ");
            for (int i = 0; i < count; i++)
                result.Append(Bytes[offset + i].ToString("X2")).Append(i + 1 == count ? '\n' : ' ');
        }
        return result.ToString().TrimEnd();
    }

    public static byte[] ParseHexEditorText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var bytes = new List<byte>();
        foreach (string sourceLine in text.Replace("\r", "").Split('\n'))
        {
            string line = sourceLine;
            int colon = line.IndexOf(':');
            if (colon >= 0) line = line[(colon + 1)..];
            foreach (string token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string clean = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
                if (clean.Length == 0 || (clean.Length & 1) != 0)
                    throw new FormatException($"Invalid hex group '{token}'. Hex groups must contain an even number of digits.");
                for (int i = 0; i < clean.Length; i += 2)
                {
                    string byteText = clean.Substring(i, 2);
                    if (!byte.TryParse(byteText, System.Globalization.NumberStyles.HexNumber, null, out byte value))
                        throw new FormatException($"Invalid hex group '{token}'. Only hexadecimal digits 0-9 and A-F are allowed.");
                    bytes.Add(value);
                }
            }
        }
        return bytes.ToArray();
    }

    private static IReadOnlyList<AtelInstruction> ParseInstructions(byte[] bytes, int codeOffset, int codeLength)
    {
        var result = new List<AtelInstruction>();
        int cursor = 0;
        while (cursor < codeLength)
        {
            byte opcode = bytes[codeOffset + cursor];
            int length = (opcode & 0x80) != 0 ? 3 : 1;
            if (cursor + length > codeLength)
                throw new InvalidOperationException($"Instruction at script offset 0x{cursor:X4} extends beyond the script code.");
            byte[] instructionBytes = bytes.AsSpan(codeOffset + cursor, length).ToArray();
            result.Add(new AtelInstruction(cursor, opcode, instructionBytes));
            cursor += length;
        }
        return result;
    }

    internal static ushort ReadUInt16(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, 2, "16-bit value");
        return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
    }

    internal static int ReadInt32(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, 4, "32-bit value");
        return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        RequireRange(bytes, offset, 4, "32-bit value");
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static int InferMissingScriptCodeLength(byte[] bytes, int codeOffset, IReadOnlyList<AtelWorker> workers)
    {
        var boundaries = workers
            .SelectMany(w => new[] { w.SharedDataOffset, w.PrivateDataOffset })
            .Where(o => o > codeOffset && o <= bytes.Length)
            .ToList();
        int boundary = boundaries.Count > 0 ? boundaries.Min() : bytes.Length;
        int end = boundary;
        while (end > codeOffset && bytes[end - 1] == 0) end--;
        if (end <= codeOffset)
            throw new InvalidOperationException("The ATEL code-length field is zero and no script-code boundary could be recovered.");
        return end - codeOffset;
    }

    internal static void RequireRange(byte[] bytes, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidOperationException($"Invalid {description} range: offset 0x{offset:X}, length 0x{length:X}, file length 0x{bytes.Length:X}.");
    }

    private static string ReadNullTerminatedUtf8(byte[] bytes, int offset)
    {
        if (offset <= 0 || offset >= bytes.Length) return string.Empty;
        int end = Array.IndexOf(bytes, (byte)0, offset);
        if (end < 0) end = bytes.Length;
        return Encoding.UTF8.GetString(bytes, offset, end - offset);
    }
}

public sealed record AtelInstruction(int Offset, byte Opcode, byte[] Bytes) : INotifyPropertyChanged
{
    private bool _isJumpDestination;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsJumpDestination
    {
        get => _isJumpDestination;
        internal set
        {
            if (_isJumpDestination == value) return;
            _isJumpDestination = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsJumpDestination)));
        }
    }
    public string Translation { get; internal set; } = "";
    public string SemanticOperandDisplay { get; internal set; } = "";
    public bool HasOperand => Bytes.Length == 3;
    public ushort Operand => HasOperand ? (ushort)(Bytes[1] | Bytes[2] << 8) : (ushort)0;
    public string OpcodeName => AtelOpcodeNames.GetName(Opcode);
    public string OperandDisplay => HasOperand ? $"0x{Operand:X4} ({Operand})" : "—";
    public string Display => $"{Offset:X4}  {string.Join(' ', Bytes.Select(b => b.ToString("X2")))}  {OpcodeName}{(HasOperand ? $"  operand={OperandDisplay}" : "")}  →  {Translation}";
    public string CompactDisplay => $"{Offset:X4}  {string.Join(' ', Bytes.Select(b => b.ToString("X2"))),-8}  {OpcodeName,-18}{(HasOperand ? $"operand={(string.IsNullOrEmpty(SemanticOperandDisplay) ? OperandDisplay : SemanticOperandDisplay)}" : "")}";
    public string OffsetDisplay => $"{Offset:X4}  ";
    public string FirstByteDisplay => Bytes[0].ToString("X2");
    public string RemainingBytesDisplay
    {
        get
        {
            string remainingBytes = string.Join(' ', Bytes.Skip(1).Select(b => b.ToString("X2")));
            string byteField = string.IsNullOrEmpty(remainingBytes) ? "" : $" {remainingBytes}";
            return $"{byteField,-6}  ";
        }
    }
    public string CompactSemanticDisplay =>
        $"{OpcodeName,-18}{(HasOperand ? $"operand={(string.IsNullOrEmpty(SemanticOperandDisplay) ? OperandDisplay : SemanticOperandDisplay)}" : "")}";
    public string CompactDisplayAfterFirstByte
    {
        get
        {
            string remainingBytes = string.Join(' ', Bytes.Skip(1).Select(b => b.ToString("X2")));
            string byteField = string.IsNullOrEmpty(remainingBytes) ? "" : $" {remainingBytes}";
            return $"{byteField,-6}  {OpcodeName,-18}{(HasOperand ? $"operand={(string.IsNullOrEmpty(SemanticOperandDisplay) ? OperandDisplay : SemanticOperandDisplay)}" : "")}";
        }
    }
}

public sealed record AtelInstructionReplacement(int ScriptOffset, byte Opcode, ushort Operand);

public sealed record AtelStatement : INotifyPropertyChanged
{
    private bool _isJumpDestination;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsJumpDestination
    {
        get => _isJumpDestination;
        internal set
        {
            if (_isJumpDestination == value) return;
            _isJumpDestination = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsJumpDestination)));
        }
    }
    public IReadOnlyList<AtelInstruction> Instructions { get; }
    public string Translation { get; }
    public int Offset => Instructions[0].Offset;
    public int ByteLength => Instructions.Sum(i => i.Bytes.Length);
    public string BytesDisplay => string.Join(' ', Instructions.SelectMany(i => i.Bytes).Select(b => b.ToString("X2")));
    public string Display => $"{Offset:X4}  {BytesDisplay,-34}  {Translation}";
    public string OffsetDisplay => $"{Offset:X4}  ";
    public string FirstByteDisplay => Instructions[0].Bytes[0].ToString("X2");
    public string RemainingBytesDisplay
    {
        get
        {
            string remainingBytes = string.Join(' ', Instructions.SelectMany(i => i.Bytes).Skip(1).Select(b => b.ToString("X2")));
            string byteField = string.IsNullOrEmpty(remainingBytes) ? "" : $" {remainingBytes}";
            return $"{byteField,-32}  ";
        }
    }
    public string DisplayAfterFirstByte
    {
        get
        {
            string remainingBytes = string.Join(' ', Instructions.SelectMany(i => i.Bytes).Skip(1).Select(b => b.ToString("X2")));
            string byteField = string.IsNullOrEmpty(remainingBytes) ? "" : $" {remainingBytes}";
            return $"{byteField,-32}  {Translation}";
        }
    }

    public AtelStatement(IReadOnlyList<AtelInstruction> instructions, string translation)
    {
        Instructions = instructions;
        Translation = translation;
    }
}

public sealed class AtelWorker
{
    public int Index { get; init; }
    public int EventType { get; init; }
    public int VariableCount { get; init; }
    public int IntegerConstantCount { get; init; }
    public int FloatConstantCount { get; init; }
    public int FunctionCount { get; init; }
    public int JumpCount { get; init; }
    public int PrivateDataLength { get; init; }
    public int PrivateDataOffset { get; init; }
    public int SharedDataOffset { get; init; }
    public IReadOnlyList<int> FloatConstantBits { get; init; } = [];
    public int FloatConstantOffset { get; init; }
    public IReadOnlyList<int> FunctionOffsets { get; init; } = [];
    public IReadOnlyList<int> JumpOffsets { get; init; } = [];
    public int FunctionTableOffset { get; init; }
    public int JumpTableOffset { get; init; }
    public int HeaderOffset { get; init; }
    public int? BattleWorkerType { get; internal set; }
    public int? PurposeSlot { get; internal set; }
    internal Dictionary<int, int> FunctionBattleSlots { get; } = [];
    public string Display
    {
        get
        {
            string parserMetadata = BattleWorkerType.HasValue
                ? $"Battle, Type={BattleWorkerTypeName(BattleWorkerType.Value)} [{BattleWorkerType.Value:X2}h]" +
                  (PurposeSlot.HasValue ? $", PurposeSlot={BattleWorkerSlotName(PurposeSlot.Value)} [{PurposeSlot.Value:X2}h]" : "")
                : $"type={EventType:X2}";
            return $"w{Index:X2}: {parserMetadata}, functions={FunctionCount}, jumps={JumpCount}, vars={VariableCount}, private=0x{PrivateDataOffset:X}/0x{PrivateDataLength:X}";
        }
    }

    private static string BattleWorkerTypeName(int value) => value switch
    {
        0x00 => "CameraHandler", 0x01 => "MotionHandler", 0x02 => "CombatHandler",
        0x03 => "BattleGruntHandler", 0x04 => "BattleScenes", 0x05 => "VoiceHandler",
        0x06 => "StartEndHooks", 0x07 => "MagicCameraHandler-Command",
        0x08 => "MagicCameraHandler-Item", 0x09 => "MagicCameraHandler-Monmagic1",
        0x0A => "MagicCameraHandler-Monmagic2", _ => $"battleWorkerType:{value}"
    };

    private static string BattleWorkerSlotName(int value)
    {
        if (value is >= 0x41 and <= 0x43) return $"MagicCam3-{value - 0x41}";
        if (value is >= 0x44 and <= 0x46) return $"MagicCam2-{value - 0x44}";
        if (value is >= 0x47 and <= 0x49) return $"MagicCam4-{value - 0x47}";
        if (value is >= 0x4A and <= 0x4C) return $"MagicCam6-{value - 0x4A}";
        return value switch
        {
            0x00 => "?BattleCameras", 0x04 => "?MonsterMotionHandles1",
            0x3D => "MonsterAi", 0x3E => "BtlScene0-7", 0x3F => "BtlScene8+ (Voice)",
            0x40 => "?MonsterMotionHandles2", 0x6C => "?StartEndHooks1",
            0x89 => "?StartEndHooks2", _ => $"battleWorkerSlot:{value}"
        };
    }

    public string FunctionName(int functionIndex)
    {
        if (functionIndex == 0) return "init";
        if (functionIndex == 1) return "main";
        if (!BattleWorkerType.HasValue || !FunctionBattleSlots.TryGetValue(functionIndex, out int slot))
            return $"f{functionIndex:X2}";

        string? tag = BattleWorkerType.Value switch
        {
            0x00 => CameraHandlerTag(slot) is string camera ? "Cam" + camera : null,
            0x01 => MotionHandlerTag(slot) is string motion ? "Motion" + motion : null,
            0x02 => CombatHandlerTag(slot),
            0x03 => BattleGruntHandlerTag(slot) is string grunt ? "Grunt" + grunt : null,
            0x04 => $"btlScene{slot:X2}",
            0x06 => BattleStartEndHookTag(slot) is string hook ? "Hook" + hook : null,
            _ => null
        };
        if (tag != null) return tag;
        if (PurposeSlot == 0x00) return $"?BattleCam{slot:X2}";
        return $"t{BattleWorkerType.Value:X2}p{slot:X2}";
    }

    private static string? CombatHandlerTag(int slot) => slot switch
    {
        0x00 => "onTurn", 0x01 => "preTurn", 0x02 => "onTargeted", 0x03 => "onHit",
        0x04 => "onDeath", 0x05 => "onMove", 0x06 => "postTurn", 0x07 => "postMove?",
        0x08 => "postPoison", 0x09 => "YojiPay", 0x0A => "YojiDismiss", 0x0B => "YojiDeath",
        0x0C => "MagusTurn", 0x0D => "MagusDoAsYouWill", 0x0E => "MagusOneMoreTime",
        0x0F => "MagusFight", 0x10 => "MagusGoGo", 0x11 => "MagusHelpEachOther",
        0x12 => "MagusCombinePowers", 0x13 => "MagusDefense", 0x14 => "MagusAreYouAllRight",
        _ => null
    };

    private static string? MotionHandlerTag(int slot) => slot switch
    {
        0x00 => "Wait", 0x04 => "Magic", 0x05 => "MagicThrow", 0x13 => "SP1",
        0x14 => "SP2", 0x15 => "SP3", 0x16 => "SP4", 0x17 => "SP5", 0x18 => "SP6",
        0x19 => "SP7", 0x1A => "SP8", 0x41 => "AttackEnd", 0x4E => "Pay", _ => null
    };

    private static string? CameraHandlerTag(int slot) => slot switch
    {
        0x18 => "Enter", 0x19 => "Select", 0x1B => "MagicStart", 0x1C => "Normal",
        0x2C => "MonMagicStart", 0x2D => "MonMagicLaunch", 0x2E => "MonItemStart",
        0x2F => "MonItemLaunch", 0x33 => "ItemLaunch", 0x34 => "MagicLaunch",
        0x36 => "Swap", 0x3C => "SkillActivation", 0x42 => "PlayerVictory",
        0x43 => "PlayerDefeat", 0x79 => "SummonMagicFiring", 0x83 => "Summon", _ => null
    };

    private static string? BattleGruntHandlerTag(int slot) => slot switch
    {
        0x09 => "OnAttack", 0x0A => "AfterAttack", 0x0B => "OnDamaged", _ => null
    };

    private static string? BattleStartEndHookTag(int slot) => slot switch
    {
        0x04 => "End", 0x05 => "Start", _ => null
    };

    internal static AtelWorker Read(byte[] bytes, int index, int offset)
    {
        AtelScriptDocument.RequireRange(bytes, offset, AtelScriptDocument.WorkerHeaderLength, $"worker {index} header");
        int functionCount = AtelScriptDocument.ReadUInt16(bytes, offset + 0x08);
        int jumpCount = AtelScriptDocument.ReadUInt16(bytes, offset + 0x0A);
        int functionTable = AtelScriptDocument.ReadInt32(bytes, offset + 0x20);
        int jumpTable = AtelScriptDocument.ReadInt32(bytes, offset + 0x24);
        return new AtelWorker
        {
            Index = index,
            HeaderOffset = offset,
            EventType = AtelScriptDocument.ReadUInt16(bytes, offset),
            VariableCount = AtelScriptDocument.ReadUInt16(bytes, offset + 0x02),
            IntegerConstantCount = AtelScriptDocument.ReadUInt16(bytes, offset + 0x04),
            FloatConstantCount = AtelScriptDocument.ReadUInt16(bytes, offset + 0x06),
            FunctionCount = functionCount,
            JumpCount = jumpCount,
            PrivateDataLength = AtelScriptDocument.ReadInt32(bytes, offset + 0x10),
            PrivateDataOffset = AtelScriptDocument.ReadInt32(bytes, offset + 0x2C),
            SharedDataOffset = AtelScriptDocument.ReadInt32(bytes, offset + 0x30),
            FloatConstantOffset = AtelScriptDocument.ReadInt32(bytes, offset + 0x1C),
            FunctionTableOffset = functionTable,
            JumpTableOffset = jumpTable,
            FloatConstantBits = ReadInt32Table(bytes, AtelScriptDocument.ReadInt32(bytes, offset + 0x1C),
                AtelScriptDocument.ReadUInt16(bytes, offset + 0x06), $"worker {index} float constants"),
            FunctionOffsets = ReadOffsetTable(bytes, functionTable, functionCount, $"worker {index} function table"),
            JumpOffsets = ReadOffsetTable(bytes, jumpTable, jumpCount, $"worker {index} jump table")
        };
    }

    private static IReadOnlyList<int> ReadInt32Table(byte[] bytes, int offset, int count, string description)
    {
        if (count == 0) return [];
        AtelScriptDocument.RequireRange(bytes, offset, checked(count * 4), description);
        return Enumerable.Range(0, count).Select(i => AtelScriptDocument.ReadInt32(bytes, offset + i * 4)).ToArray();
    }

    private static IReadOnlyList<int> ReadOffsetTable(byte[] bytes, int offset, int count, string description)
    {
        if (count == 0) return [];
        AtelScriptDocument.RequireRange(bytes, offset, checked(count * 4), description);
        return Enumerable.Range(0, count).Select(i => AtelScriptDocument.ReadInt32(bytes, offset + i * 4)).ToArray();
    }
}
