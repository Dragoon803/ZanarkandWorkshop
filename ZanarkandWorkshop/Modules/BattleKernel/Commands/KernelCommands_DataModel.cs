using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Modules.BattleKernel.Commands
{
    internal partial class KernelCommands_DataModel : ObservableObject
    {
        public Process_Service ProcService { get => Process_Service.Instance; }
        /******************************************
         * Data
         ******************************************/
        CommandFile_enum CommandFileType { get; set; }
        List<Ability_Command> CommandsList { get; set; }
        List<KernelCommands_Wrapper> LoadedCommands { get; set; }
        internal ObservableCollection<KernelCommands_Wrapper> DisplayedCommands { get; set; }

        /******************************************
         * View settings
         ******************************************/
        [ObservableProperty] public bool showDescription = true;
        [ObservableProperty] public bool showAnimations = false;
        [ObservableProperty] public bool showMenu = false;
        [ObservableProperty] public bool showTargetProperties = false;
        [ObservableProperty] public bool showProperties = false;
        [ObservableProperty] public bool showCosts = false;
        [ObservableProperty] public bool showAttackData = false;
        [ObservableProperty] public bool showElement = false;
        [ObservableProperty] public bool showStatus = false;
        [ObservableProperty] public bool showStatusTemporary = false;
        [ObservableProperty] public bool showStatusSpecial = false;
        [ObservableProperty] public bool showStatBuffs = false;
        [ObservableProperty] public bool showMixBuffs = false;
        [ObservableProperty] public bool showExtra = false;
        [ObservableProperty] public bool showItemPreview = false;
        [ObservableProperty] public string recoveryStatus = "";
        [ObservableProperty] public string filterText = "";
        [ObservableProperty] public KernelCommands_Wrapper? selectedCommand;
        [ObservableProperty] public bool isDirty;
        private byte[] _baselineFile = Array.Empty<byte>();
        private byte[] _historyState = Array.Empty<byte>();
        private sealed record CommandHistoryEntry(
            byte[] Before,
            byte[] After,
            int? CommandIndex,
            string Description);

        private readonly Stack<CommandHistoryEntry> _undoHistory = new();
        private readonly Stack<CommandHistoryEntry> _redoHistory = new();
        private bool _restoringHistory;

        public bool CanUndo => _undoHistory.Count > 0;
        public bool CanRedo => _redoHistory.Count > 0;
        public bool CanUndoAll => IsDirty;

        public List<string> CharacterOptions => new Character_Converter().Options.Values.ToList();
        public List<string> HitCalcTypeOptions => new HitCalcType_Converter().Options.Values.ToList();
        public List<string> DamageFormulaOptions => new DamageFormula_Converter().Options.Values.ToList();

        public bool IsExtraEnabled => HasExtraInfo();
        public bool IsItemPreviewEnabled => CommandFileType == CommandFile_enum.Item;
        public bool IsMonsterCommandEditor =>
            CommandFileType is CommandFile_enum.MonMagic1 or CommandFile_enum.MonMagic2;
        public bool ShowMenuExtra => ShowMenu && IsExtraEnabled;
        private int ProtectedMonsterCommandCount => CommandFileType switch
        {
            CommandFile_enum.MonMagic1 => 300,
            CommandFile_enum.MonMagic2 => 247,
            _ => int.MaxValue
        };
        public bool CanDeleteSelectedClone
        {
            get
            {
                if (!IsMonsterCommandEditor || SelectedCommand == null)
                    return false;

                int actualIndex = LoadedCommands.IndexOf(SelectedCommand);
                return actualIndex >= ProtectedMonsterCommandCount &&
                       actualIndex == LoadedCommands.Count - 1;
            }
        }

        partial void OnShowMenuChanged(bool value) => OnPropertyChanged(nameof(ShowMenuExtra));
        partial void OnSelectedCommandChanged(KernelCommands_Wrapper? value) =>
            OnPropertyChanged(nameof(CanDeleteSelectedClone));

        public KernelCommands_DataModel(CommandFile_enum commandFileType)
        {
            CommandFileType = commandFileType;

            LoadedCommands = new();
            DisplayedCommands = new();
            LoadCommands();
            ApplyFilter();
        }

        public void LoadCommands()
        {
            byte[] byteFile = File.ReadAllBytes(GetFilePath());
            LoadCommandsFromBytes(byteFile, true);
        }

        private void LoadCommandsFromBytes(byte[] byteFile, bool resetBaseline)
        {
            int? selectedIndex = SelectedCommand?.Index;
            CommandsList = Ability_Command.ReadList(byteFile, HasExtraInfo());

            LoadedCommands.Clear();
            for (int i = 0; i < CommandsList.Count; i++)
            {
                KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(CommandsList[i]);
                wrapper.Index = i;
                wrapper.PropertyChanged += CommandChanged;
                LoadedCommands.Add(wrapper);
            }
            ApplyFilter();
            SelectedCommand = selectedIndex is int index && index >= 0 && index < LoadedCommands.Count
                ? LoadedCommands[index]
                : null;
            _historyState = BuildFile();
            if (resetBaseline)
                _baselineFile = _historyState.ToArray();
            IsDirty = !_historyState.SequenceEqual(_baselineFile);
            if (resetBaseline)
            {
                _undoHistory.Clear();
                _redoHistory.Clear();
            }
            NotifyHistoryState();
        }

        public void ApplyFilter()
        {
            List<KernelCommands_Wrapper> desired = LoadedCommands.Where(command =>
                FilterText == "" ||
                    command.Index.ToString().Contains(FilterText.ToLower()) ||
                    command.Name.ToLower().Contains(FilterText.ToLower()) ||
                    command.Description.ToLower().Contains(FilterText.ToLower()))
                .ToList();

            // Avoid CollectionChanged.Reset. Avalonia's DataGrid can retain
            // recycled cell presenters from hidden column groups after a full
            // Clear/rebuild, which was the source of the stretched-row Undo
            // glitch. Replace rows in place and only add/remove the tail.
            int sharedCount = Math.Min(DisplayedCommands.Count, desired.Count);
            for (int i = 0; i < sharedCount; i++)
            {
                if (!ReferenceEquals(DisplayedCommands[i], desired[i]))
                    DisplayedCommands[i] = desired[i];
            }
            while (DisplayedCommands.Count > desired.Count)
                DisplayedCommands.RemoveAt(DisplayedCommands.Count - 1);
            for (int i = DisplayedCommands.Count; i < desired.Count; i++)
                DisplayedCommands.Add(desired[i]);
        }

        public KernelCommands_Wrapper CloneAsNewCommand(KernelCommands_Wrapper source)
        {
            if (!IsMonsterCommandEditor)
                throw new InvalidOperationException(
                    "New command slots are supported only for monster-command files.");
            if (LoadedCommands.Count >= 0x1000)
                throw new InvalidOperationException(
                    "The monster-command file has no remaining 12-bit command indices.");

            int sourceIndex = LoadedCommands.IndexOf(source);
            if (sourceIndex < 0)
                throw new InvalidOperationException(
                    "The selected command is not part of the loaded command file.");

            Ability_Command sourceCommand = source.Unwrap();
            Ability_Command clone = Ability_Command.ReadSingle(
                sourceCommand.WriteSingle(hasExtraInfo: false), hasExtraInfo: false);
            KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(clone);
            wrapper.Name = $"{source.Name} - clone";
            wrapper.Index = LoadedCommands.Count;
            wrapper.PropertyChanged += CommandChanged;
            LoadedCommands.Add(wrapper);

            FilterText = "";
            ApplyFilter();
            int category = CommandFileType == CommandFile_enum.MonMagic1 ? 0x4 : 0x6;
            int commandReference = (category << 12) | wrapper.Index;
            RecoveryStatus =
                $"Cloned command {sourceIndex} into new slot {wrapper.Index} " +
                $"(reference 0x{commandReference:X4}). Save to write it to disk.";
            RefreshDirtyState($"created command #{wrapper.Index}", wrapper.Index);
            return wrapper;
        }

        public KernelCommands_Wrapper? DeleteClonedCommand(KernelCommands_Wrapper command)
        {
            if (!IsMonsterCommandEditor)
                throw new InvalidOperationException(
                    "Cloned-command deletion is supported only for monster-command files.");

            int actualIndex = LoadedCommands.IndexOf(command);
            if (actualIndex < 0)
                throw new InvalidOperationException(
                    "The selected command is not part of the loaded command file.");
            if (actualIndex < ProtectedMonsterCommandCount)
                throw new InvalidOperationException(
                    $"Command {actualIndex} is an original game command and cannot be deleted.");
            if (actualIndex != LoadedCommands.Count - 1)
                throw new InvalidOperationException(
                    "Only the final cloned command can be deleted. Deleting an earlier clone " +
                    "would change later command IDs and break battle-script references.");

            LoadedCommands.RemoveAt(actualIndex);
            FilterText = "";
            ApplyFilter();
            RecoveryStatus =
                $"Deleted cloned command {actualIndex}. Save to write the removal to disk.";
            RefreshDirtyState($"deleted command #{actualIndex}", actualIndex);
            return LoadedCommands.Count > 0 ? LoadedCommands[^1] : null;
        }

        public void Save()
        {
            string path = GetFilePath();
            byte[] rebuilt = BuildFile();
            _ = Ability_Command.ReadList(rebuilt, HasExtraInfo());
            File.WriteAllBytes(path, rebuilt);
            _baselineFile = rebuilt.ToArray();
            _historyState = rebuilt.ToArray();
            _undoHistory.Clear();
            _redoHistory.Clear();
            IsDirty = false;
            NotifyHistoryState();
            RecoveryStatus = EditorSaveStatus.Success(GetEditorName());
        }

        public void SaveToMaster(string masterPath)
        {
            byte[] rebuilt = BuildFile();
            _ = Ability_Command.ReadList(rebuilt, HasExtraInfo());
            string fileName = CommandFileType switch
            {
                CommandFile_enum.Command => "command.bin",
                CommandFile_enum.Item => "item.bin",
                CommandFile_enum.MonMagic1 => "monmagic1.bin",
                CommandFile_enum.MonMagic2 => "monmagic2.bin",
                _ => throw new InvalidOperationException("Command file type is not selected.")
            };
            File.WriteAllBytes(Path.Combine(masterPath, "new_uspc", "battle", "kernel", fileName), rebuilt);
        }

        public void RestoreOriginalAndSave(string originalPath)
        {
            if (!File.Exists(originalPath))
                throw new InvalidOperationException($"Original file was not found: {originalPath}");

            byte[] originalBytes = File.ReadAllBytes(originalPath);
            List<Ability_Command> verified = Ability_Command.ReadList(originalBytes, HasExtraInfo());
            if (verified.Count == 0)
                throw new InvalidDataException("The original file contains no readable entries.");

            string projectPath = GetFilePath();
            File.WriteAllBytes(projectPath, originalBytes);

            LoadCommands();
            ApplyFilter();
            RecoveryStatus = $"Restored {GetEditorName()} from verified Original Game Files.";
        }
        public void LoadInGame()
        {
            int fileAddress;
            if (CommandFileType == CommandFile_enum.Item)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_ITEM);
            }
            else if (CommandFileType == CommandFile_enum.Command)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_COMMAND);
            }
            else if (CommandFileType == CommandFile_enum.MonMagic1)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_MONMAGIC1);
            }
            else if (CommandFileType == CommandFile_enum.MonMagic2)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_MONMAGIC2);
            }
            else return;

            MemSharp_Service.Instance.Write(fileAddress, BuildFile(), false);
        }

        private byte[] BuildFile()
        {
            List<Ability_Command> commandList = new();
            for (int i = 0; i < LoadedCommands.Count; i++)
            {
                Ability_Command command = LoadedCommands[i].Unwrap();
                commandList.Add(command);
            }

            bool hasExtraInfo = HasExtraInfo();

            return Ability_Command.WriteList(commandList, hasExtraInfo);
        }

        private void CommandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not KernelCommands_Wrapper command || e.PropertyName == nameof(KernelCommands_Wrapper.Index))
                return;

            int index = LoadedCommands.IndexOf(command);
            string field = FriendlyFieldName(e.PropertyName);
            RefreshDirtyState($"changed command #{index} {field}", index);
        }

        public void MarkActiveCellDirty() => IsDirty = true;

        public void RefreshDirtyState(string? description = null, int? commandIndex = null)
        {
            byte[] current = BuildFile();
            if (!_restoringHistory && !_historyState.SequenceEqual(current))
            {
                _undoHistory.Push(new CommandHistoryEntry(
                    _historyState.ToArray(),
                    current.ToArray(),
                    commandIndex,
                    description ?? "changed command data"));
                _redoHistory.Clear();
                _historyState = current.ToArray();
            }
            IsDirty = !current.SequenceEqual(_baselineFile);
            NotifyHistoryState();
        }

        public void Undo()
        {
            if (_undoHistory.Count == 0) return;
            CommandHistoryEntry entry = _undoHistory.Pop();
            _redoHistory.Push(entry);
            RestoreHistory(entry.Before, entry.CommandIndex);
            RecoveryStatus = $"Undid: {entry.Description}.";
        }

        public void Redo()
        {
            if (_redoHistory.Count == 0) return;
            CommandHistoryEntry entry = _redoHistory.Pop();
            _undoHistory.Push(entry);
            RestoreHistory(entry.After, entry.CommandIndex);
            RecoveryStatus = $"Redid: {entry.Description}.";
        }

        public void UndoAll()
        {
            if (!IsDirty) return;
            int count = _undoHistory.Count;

            // Rewind the session without collapsing its entries. Moving the
            // newest applied entry first leaves the oldest entry on top of the
            // Redo stack, so Redo can replay every original edit one at a time.
            // Any entries already in Redo remain after those applied entries,
            // preserving the complete chronological sequence.
            while (_undoHistory.Count > 0)
                _redoHistory.Push(_undoHistory.Pop());

            RestoreHistory(_baselineFile, null);
            NotifyHistoryState();
            RecoveryStatus = $"Undid all: {count} command change{(count == 1 ? "" : "s")} since the last save.";
        }

        private void RestoreHistory(byte[] snapshot, int? commandIndex)
        {
            _restoringHistory = true;
            try { RestoreCommandsInPlace(snapshot, commandIndex); }
            finally { _restoringHistory = false; }
            _historyState = BuildFile();
            NotifyHistoryState();
        }

        private void RestoreCommandsInPlace(byte[] snapshot, int? commandIndex)
        {
            int? selectedIndex = SelectedCommand?.Index;
            List<Ability_Command> restored = Ability_Command.ReadList(snapshot, HasExtraInfo());

            if (commandIndex is int index &&
                restored.Count == LoadedCommands.Count &&
                index >= 0 && index < restored.Count)
            {
                ReplaceCommandWrapper(index, restored[index]);
            }
            else
            {
                int sharedCount = Math.Min(LoadedCommands.Count, restored.Count);
                for (int i = 0; i < sharedCount; i++)
                    ReplaceCommandWrapper(i, restored[i]);

                while (LoadedCommands.Count > restored.Count)
                    LoadedCommands.RemoveAt(LoadedCommands.Count - 1);
                for (int i = LoadedCommands.Count; i < restored.Count; i++)
                    LoadedCommands.Add(CreateWrapper(restored[i], i));
            }

            CommandsList = restored;
            ApplyFilter();
            SelectedCommand = selectedIndex is int selected && selected >= 0 && selected < LoadedCommands.Count
                ? LoadedCommands[selected]
                : null;
            IsDirty = !snapshot.SequenceEqual(_baselineFile);
        }

        private void ReplaceCommandWrapper(int index, Ability_Command command)
        {
            LoadedCommands[index].PropertyChanged -= CommandChanged;
            LoadedCommands[index] = CreateWrapper(command, index);
        }

        private KernelCommands_Wrapper CreateWrapper(Ability_Command command, int index)
        {
            KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(command);
            wrapper.Index = index;
            wrapper.PropertyChanged += CommandChanged;
            return wrapper;
        }

        private static string FriendlyFieldName(string? propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return "data";

            string name = propertyName;
            foreach (string prefix in new[] { "FlagMenu", "FlagTarget", "FlagUsage", "FlagMisc1", "FlagMisc2", "FlagMisc3", "FlagMisc4", "FlagDamage", "FlagElement", "FlagStatus", "FlagPreview", "Flag" })
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    name = name[prefix.Length..];
                    break;
                }
            }

            name = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
            return name.ToLowerInvariant();
        }

        private void NotifyHistoryState()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(CanUndoAll));
        }

        public string GetFilePath()
        {
            if(CommandFileType == CommandFile_enum.Command)
            {
                return Project_Service.Instance.Path_KernelCommandUs;
            }
            if (CommandFileType == CommandFile_enum.Item)
            {
                return Project_Service.Instance.Path_KernelItemUs;
            }
            if (CommandFileType == CommandFile_enum.MonMagic1)
            {
                return Project_Service.Instance.Path_KernelMonMagic1Us;
            }
            if (CommandFileType == CommandFile_enum.MonMagic2)
            {
                return Project_Service.Instance.Path_KernelMonMagic2Us;
            }

            throw new System.Exception("[KernelCommands_DataModel] File type not selected");
        }

        public string GetEditorName() => CommandFileType switch
        {
            CommandFile_enum.Command => "Player & Aeon Commands",
            CommandFile_enum.Item => "Items",
            CommandFile_enum.MonMagic1 => "Standard Monster Commands",
            CommandFile_enum.MonMagic2 => "Boss Commands",
            _ => "Kernel editor"
        };

        public bool HasExtraInfo()
        {
            if (CommandFileType == CommandFile_enum.Command || CommandFileType == CommandFile_enum.Item)
            {
                return true;
            }
            if (CommandFileType == CommandFile_enum.MonMagic1 || CommandFileType == CommandFile_enum.MonMagic2)
            {
                return false;
            }

            throw new System.Exception("[KernelCommands_DataModel] File type not selected");
        }
    }
}
