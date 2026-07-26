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

        public List<string> CharacterOptions => new Character_Converter().Options.Values.ToList();
        public List<string> HitCalcTypeOptions => new HitCalcType_Converter().Options.Values.ToList();
        public List<string> DamageFormulaOptions => new DamageFormula_Converter().Options.Values.ToList();

        public bool IsExtraEnabled => HasExtraInfo();
        public bool IsItemPreviewEnabled => CommandFileType == CommandFile_enum.Item;
        public bool ShowMenuExtra => ShowMenu && IsExtraEnabled;
        string FilterText { get; set; } = "";

        partial void OnShowMenuChanged(bool value) => OnPropertyChanged(nameof(ShowMenuExtra));

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
            CommandsList = Ability_Command.ReadList(byteFile, HasExtraInfo());

            LoadedCommands.Clear();
            for (int i = 0; i < CommandsList.Count; i++)
            {
                KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(CommandsList[i]);
                wrapper.Index = i;
                LoadedCommands.Add(wrapper);
            }
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

        public void Save()
        {
            string path = GetFilePath();
            byte[] rebuilt = BuildFile();
            _ = Ability_Command.ReadList(rebuilt, HasExtraInfo());
            CreateBackupIfNeeded(path);
            File.WriteAllBytes(path, rebuilt);
            RecoveryStatus = $"Saved and verified. Original project backup: {path}.bak";
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
            CreateBackupIfNeeded(projectPath);
            File.WriteAllBytes(projectPath, originalBytes);

            LoadCommands();
            ApplyFilter();
            RecoveryStatus = $"Restored {GetEditorName()} from verified Original Game Files. " +
                $"Previous project file: {projectPath}.bak";
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

        private static void CreateBackupIfNeeded(string path)
        {
            string backupPath = path + ".bak";
            if (!File.Exists(backupPath))
                File.Copy(path, backupPath);
        }

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
