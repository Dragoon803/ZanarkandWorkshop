using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules
{
    public partial class GameIndex_Wrapper : ObservableObject
    {
        private IReadOnlyList<MenuAbilityOption> _menuAbilityOptions = [];
        private IReadOnlyList<AutoAbilityDropOption> _autoAbilityDropOptions = [];

        [ObservableProperty][NotifyPropertyChangedFor(nameof(Name))][NotifyPropertyChangedFor(nameof(Value))] public byte category;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Name))][NotifyPropertyChangedFor(nameof(Value))] public ushort index;

        string Name => FfxCommon_Util.GetGameIndexName(category, index);
        public IReadOnlyList<MenuAbilityCategoryOption> MenuAbilityCategories => MenuAbilityCategoryOption.All;
        public IReadOnlyList<MenuAbilityOption> MenuAbilityOptions => _menuAbilityOptions
            .Where(option => option.Category == Category)
            .ToArray();
        public bool IsMenuAbilityCommandEnabled => Category != 0 && MenuAbilityOptions.Any(option => option.IsKnown);
        public string MenuAbilityPlaceholder => Category == 0 ? "NONE" :
            IsMenuAbilityCommandEnabled ? "Select command" : "Commands unavailable";
        public IReadOnlyList<AutoAbilityDropOption> AutoAbilityDropOptions => _autoAbilityDropOptions;
        public bool IsAutoAbilityDropEnabled => _autoAbilityDropOptions.Any(option => option.IsKnown && option.Value != 0);
        public string AutoAbilityDropPlaceholder => IsAutoAbilityDropEnabled ? "Select auto ability" : "Auto abilities unavailable";
        public AutoAbilityDropOption? SelectedAutoAbilityDrop
        {
            get => _autoAbilityDropOptions.FirstOrDefault(option => option.Value == Value);
            set
            {
                if (value == null) return;
                Value = value.Value;
                OnPropertyChanged();
            }
        }
        public MenuAbilityCategoryOption? SelectedMenuAbilityCategory
        {
            get => MenuAbilityCategoryOption.All.FirstOrDefault(option => option.Category == Category);
            set
            {
                if (value == null || value.Category == Category) return;
                Category = value.Category;
                OnPropertyChanged();
            }
        }
        public MenuAbilityOption? SelectedMenuAbility
        {
            get => MenuAbilityOptions.FirstOrDefault(option => option.Index == Index);
            set
            {
                if (value == null) return;
                Category = value.Category;
                Index = value.Index;
                OnPropertyChanged();
            }
        }
        public ushort Value
        {
            get => Unwrap();
            set
            {
                Category = FfxCommon_Util.GetGameCategory(value);
                Index = FfxCommon_Util.GetGameIndex(value);
            }
        }

        public static GameIndex_Wrapper Wrap(ushort gameIndex)
        {
            GameIndex_Wrapper wrapper = new();
            wrapper.Category = FfxCommon_Util.GetGameCategory(gameIndex);
            wrapper.Index = FfxCommon_Util.GetGameIndex(gameIndex);
            return wrapper;
        }

        public void ConfigureMenuAbilities(IReadOnlyList<MenuAbilityOption> options)
        {
            _menuAbilityOptions = options;
            OnPropertyChanged(nameof(MenuAbilityOptions));
            OnPropertyChanged(nameof(IsMenuAbilityCommandEnabled));
            OnPropertyChanged(nameof(MenuAbilityPlaceholder));
            OnPropertyChanged(nameof(SelectedMenuAbilityCategory));
            OnPropertyChanged(nameof(SelectedMenuAbility));
            OnPropertyChanged(nameof(SelectedAutoAbilityDrop));
        }

        public void ConfigureAutoAbilityDrops(IReadOnlyList<AutoAbilityDropOption> options)
        {
            _autoAbilityDropOptions = options;
            OnPropertyChanged(nameof(AutoAbilityDropOptions));
            OnPropertyChanged(nameof(SelectedAutoAbilityDrop));
            OnPropertyChanged(nameof(IsAutoAbilityDropEnabled));
            OnPropertyChanged(nameof(AutoAbilityDropPlaceholder));
        }

        partial void OnCategoryChanged(byte value)
        {
            OnPropertyChanged(nameof(MenuAbilityOptions));
            MenuAbilityOption? first = MenuAbilityOptions.FirstOrDefault();
            if (first != null)
                Index = first.Index;
            OnPropertyChanged(nameof(SelectedMenuAbilityCategory));
            OnPropertyChanged(nameof(SelectedMenuAbility));
            OnPropertyChanged(nameof(IsMenuAbilityCommandEnabled));
            OnPropertyChanged(nameof(MenuAbilityPlaceholder));
        }

        partial void OnIndexChanged(ushort value)
        {
            OnPropertyChanged(nameof(SelectedMenuAbility));
            OnPropertyChanged(nameof(SelectedAutoAbilityDrop));
        }
        public ushort Unwrap()
        {
            ushort gameIndex = new();
            gameIndex = FfxCommon_Util.SetGameCategory(gameIndex, category);
            gameIndex = FfxCommon_Util.SetGameIndex(gameIndex, index);
            return gameIndex;
        }
    }

    public sealed record MenuAbilityCategoryOption(string Name, byte Category)
    {
        public static IReadOnlyList<MenuAbilityCategoryOption> All { get; } =
        [
            new("NONE", 0x0),
            new("Player & Aeon Commands", 0x3),
            new("Standard Monster Commands", 0x4),
            new("Boss Commands", 0x6),
            new("Items", 0x2)
        ];

        public override string ToString() => Name;
    }

    public sealed record MenuAbilityOption(byte Category, ushort Index, string Name, bool IsKnown = true)
    {
        public override string ToString() => Name;
    }

    public sealed record AutoAbilityDropOption(ushort Value, string Name, bool IsKnown = true)
    {
        public override string ToString() => Name;
    }
}
