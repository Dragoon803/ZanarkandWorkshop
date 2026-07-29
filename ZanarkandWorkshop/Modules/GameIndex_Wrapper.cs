using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;

namespace FFXProjectEditor.Modules
{
    public partial class GameIndex_Wrapper : ObservableObject
    {
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Name))][NotifyPropertyChangedFor(nameof(Value))] public byte category;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Name))][NotifyPropertyChangedFor(nameof(Value))] public ushort index;

        string Name => FfxCommon_Util.GetGameIndexName(category, index);
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
        public ushort Unwrap()
        {
            ushort gameIndex = new();
            gameIndex = FfxCommon_Util.SetGameCategory(gameIndex, category);
            gameIndex = FfxCommon_Util.SetGameIndex(gameIndex, index);
            return gameIndex;
        }
    }
}
