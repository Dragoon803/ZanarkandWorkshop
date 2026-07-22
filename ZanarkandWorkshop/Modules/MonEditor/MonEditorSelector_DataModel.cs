using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Modules.MonEditor
{
    public partial class MonEditorSelector_DataModel : ObservableObject
    {
        /******************************************
         * Data
         ******************************************/
        internal ObservableCollection<MonsterListEntry> LoadedMonsters { get; set; } = new();
        internal ObservableCollection<MonsterListEntry> DisplayedMonsters { get; set; } = new();

        string FilterText { get; set; } = "";

        [ObservableProperty] public bool infoExpanded = false;
        [ObservableProperty] public bool statsExpanded = true;
        [ObservableProperty] public bool propertiesExpanded = true;
        [ObservableProperty] public bool elementalWeaknessesExpanded = true;
        [ObservableProperty] public bool statusExpanded = true;
        [ObservableProperty] public bool menuAbilitiesExpanded = false;
        [ObservableProperty] public bool identifiersExpanded = true;

        [ObservableProperty] public bool lootStatsExpanded = true;
        [ObservableProperty] public bool lootDropsExpanded = true;
        [ObservableProperty] public bool lootStealExpanded = true;
        [ObservableProperty] public bool lootGearExpanded = true;        
        [ObservableProperty] public int selectedTab = 0;

        [ObservableProperty] public bool worker00Expanded = true;
        [ObservableProperty] public bool w00InitExpanded = true;
        [ObservableProperty] public bool w00MainExpanded = true;

        [ObservableProperty] public bool worker01Expanded = true;
        [ObservableProperty] public bool w01InitExpanded = true;
        [ObservableProperty] public bool w01MainExpanded = true;
        [ObservableProperty] public bool w01OnTurnExpanded = true;
        [ObservableProperty] public bool w01OnHitExpanded = true;
        [ObservableProperty] public bool w01PreTurnExpanded = true;
        [ObservableProperty] public bool w01OnTargetedExpanded = true;
        [ObservableProperty] public bool w01OnDeathExpanded = true;

        [ObservableProperty] public bool worker02Expanded = true;
        [ObservableProperty] public bool w02BattleCam1Expanded = true;

        public MonEditorSelector_DataModel()
        {
            LoadEntries();
            ApplyFilter();
        }

        public void LoadEntries()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                return;
            }

            LoadedMonsters.Clear();
			// The FFX monster table has fixed slots m000 through m360. Build the
			// selector from those slots rather than from the folders currently on
			// disk, so a missing/renamed folder remains visible and diagnosable.
            for (int i = 0; i <= 360; i++)
            {
                LoadedMonsters.Add(new MonsterListEntry(i));
            }
        }

        public void ApplyFilter()
        {
            DisplayedMonsters.Clear();
            foreach(MonsterListEntry monster in LoadedMonsters)
            {
                if (FilterText == "" || monster.Name.ToLower().Contains(FilterText.ToLower()))
                {
                    DisplayedMonsters.Add(monster);
                }
            }
        }

        public void LoadMonster(MonsterListEntry monsterEntry, ContentControl contentFrame)
        {
            string monsterPath = Project_Service.Instance.GetPathMon(monsterEntry.Index);

            byte[] byteFile = File.ReadAllBytes(monsterPath);

            contentFrame.Content = new MonEditor_Control(Monster_File.Read(byteFile), monsterPath, this);
        }

        public class MonsterListEntry
        {
            public int Index { get; set; }
            public string Name { get; set; } // TODO

            public MonsterListEntry(int index)
            {
                Index = index;
                Name = Monster_Dictionary.Instance.ContainsKey((short)Index) ? "[" + Index + "] " + Monster_Dictionary.Instance[(short)Index] : "[" + Index + "] " + "<NOT INDEXED>";
            }
        }
    }
}
