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
        ObservableCollection<KernelCommands_Wrapper> DisplayedCommands { get; set; }

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
            _baselineFile = byteFile.ToArray();
            CommandsList = Ability_Command.ReadList(byteFile, HasExtraInfo());

            LoadedCommands.Clear();
            for (int i = 0; i < CommandsList.Count; i++)
            {
                KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(CommandsList[i]);
                wrapper.Index = i;
                wrapper.PropertyChanged += CommandChanged;
                LoadedCommands.Add(wrapper);
            }
            IsDirty = false;
        }

        public void ApplyFilter()
        {
            DisplayedCommands.Clear();
            foreach (KernelCommands_Wrapper command in LoadedCommands)
            {
                if (FilterText == "" ||
                    command.Index.ToString().Contains(FilterText.ToLower()) ||
                    command.Name.ToLower().Contains(FilterText.ToLower()) ||
                    command.Description.ToLower().Contains(FilterText.ToLower()))
                {
                    DisplayedCommands.Add(command);
                }
            }
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
            RefreshDirtyState();
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
            RefreshDirtyState();
            return LoadedCommands.Count > 0 ? LoadedCommands[^1] : null;
        }

        public void Save()
        {
            string path = GetFilePath();
            byte[] rebuilt = BuildFile();
            _ = Ability_Command.ReadList(rebuilt, HasExtraInfo());
            File.WriteAllBytes(path, rebuilt);
            _baselineFile = rebuilt.ToArray();
            IsDirty = false;
            RecoveryStatus = EditorSaveStatus.Success("Player & Aeon Commands");
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

        private void CommandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            RefreshDirtyState();

        private void RefreshDirtyState() =>
            IsDirty = !BuildFile().SequenceEqual(_baselineFile);

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
