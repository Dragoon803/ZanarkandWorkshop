using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Ability;
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

        public MonEditor_DataModel(Monster_File monsterFile, string monsterPath, MonEditorSelector_DataModel selectorDM)
        {
            MonsterFile = monsterFile;
            MonsterPath = monsterPath;
            SelectorDM = selectorDM;
            MonsterStatSheet = MonsterStatSheet_Wrapper.Wrap(MonsterFile.StatSheetFile);
            MonsterLoot = MonsterLoot_Wrapper.Wrap(MonsterFile.LootFile);
            _originalAiBytes = MonsterFile.AiFile == null ? [] : (byte[])MonsterFile.AiFile.Clone();
            _originalWorkerBytes = MonsterFile.WorkerFile == null ? [] : (byte[])MonsterFile.WorkerFile.Clone();

            try
            {
                AiDocument = AtelScriptDocument.Read(MonsterFile.AiFile, MonsterFile.WorkerFile);
                AiCommandNames = LoadCommandNames();
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

            string backupPath = OriginalBackupPath;
            if (!File.Exists(backupPath))
                File.Copy(MonsterPath, backupPath);
            File.WriteAllBytes(MonsterPath, rebuilt);

            AiStatus = $"Reverted the Battle Script to the state captured when this monster was opened and saved it to disk. Original backup: {backupPath}";
        }

        public int AiUndoCount => _aiUndoHistory.Count;
        public int AiRedoCount => _aiRedoHistory.Count;

        public void RecordAiUndoCheckpoint(string description, string? selectionKind = null, int? scriptOffset = null)
        {
            if (AiDocument == null) return;
            _aiUndoHistory.Push(CaptureAiSnapshot(description, selectionKind, scriptOffset));
            _aiRedoHistory.Clear();
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

        public string OriginalBackupPath => MonsterPath + ".bak";

        public void RestoreAiFromOriginalBackup()
        {
            if (!File.Exists(OriginalBackupPath))
                throw new InvalidOperationException($"No original backup exists yet: {OriginalBackupPath}");
            Monster_File backup = Monster_File.Read(File.ReadAllBytes(OriginalBackupPath));
            if (backup.AiFile == null || backup.AiFile.Length == 0)
                throw new InvalidOperationException("The original backup contains no monster Battle Script.");
            AtelScriptDocument restored = AtelScriptDocument.Read(backup.AiFile, backup.WorkerFile);
            restored.SetCommandNameResolver(gameIndex => AiCommandNames.TryGetValue(gameIndex, out string? name) ? name : null);
            AiDocument = restored;
            MonsterFile.AiFile = (byte[])backup.AiFile.Clone();
            AiHex = restored.ToHexEditorText();
            AiSearchOffsets = [];
            AiSearchLength = 0;
            AiStatus = $"Restored only the Battle Script from original backup {OriginalBackupPath}. Press Save to write it to disk.";
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
            MonsterLoot = MonsterLoot_Wrapper.Wrap(vanilla.LootFile);
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
            AiStatus = $"Restored the complete original monster from {originalPath} and saved it to disk. Original backup: {OriginalBackupPath}";
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

        public AtelStatement InsertStatement(int insertionOffset, byte[] statementBytes, int sourceOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int oldLength = AiDocument.Bytes.Length;
            AtelStatement inserted = AiDocument.InsertStatementBytes(insertionOffset, statementBytes);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            int growth = AiDocument.Bytes.Length - oldLength;
            string storage = growth == 0 ? "existing alignment padding was used" : $"the post-code ATEL region was relocated by {growth} byte(s)";
            AiStatus = $"Inserted {statementBytes.Length} byte(s) copied from statement at script offset 0x{sourceOffset:X4} at script offset 0x{insertionOffset:X4}; {storage}, and code offsets were rebuilt.";
            return inserted;
        }

        public int DeleteStatement(int statementOffset)
        {
            if (AiDocument == null) throw new InvalidOperationException(AiStatus);
            int removedLength = AiDocument.DeleteStatement(statementOffset);
            MonsterFile.AiFile = (byte[])AiDocument.Bytes.Clone();
            AiHex = AiDocument.ToHexEditorText();
            AiStatus = $"Deleted the {removedLength}-byte statement at script offset 0x{statementOffset:X4}; later code offsets were rebuilt. Press Save to write this change to disk.";
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
            ApplyAiHex();
            MonsterFile.StatSheetFile = MonsterStatSheet.Unwrap();
            MonsterFile.LootFile = MonsterLoot.Unwrap();

            byte[] rebuilt = MonsterFile.Write();
            Monster_File roundTrip = Monster_File.Read(rebuilt);
            AtelScriptDocument.Read(roundTrip.AiFile, roundTrip.WorkerFile);

            string backupPath = MonsterPath + ".bak";
            if (!File.Exists(backupPath))
                File.Copy(MonsterPath, backupPath);
            File.WriteAllBytes(MonsterPath, rebuilt);
            AiStatus = $"Saved and verified. Original backup: {backupPath}";
        }
    }
}
