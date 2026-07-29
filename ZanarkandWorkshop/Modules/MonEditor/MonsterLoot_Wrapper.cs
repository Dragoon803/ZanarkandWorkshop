using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using System.Collections.Generic;
using System.Linq;
using static FFXProjectEditor.FfxLib.Monster.Monster_Loot;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal partial class MonsterLoot_Wrapper : ObservableObject
    {
        internal sealed record RonsoRageCommandOption(ushort CommandId, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }

        [ObservableProperty] public ushort gil;
        [ObservableProperty] public ushort ap;
        [ObservableProperty] public ushort apOverkill;
        [ObservableProperty] public RonsoRageCommandOption selectedRonsoRage = null!;
        public IReadOnlyList<RonsoRageCommandOption> RonsoRageCommands { get; private set; } = [];

        // Drops
        [ObservableProperty] public byte drop1Chance;
        [ObservableProperty] public byte drop2Chance;
        [ObservableProperty] public byte stealChance;
        [ObservableProperty] public byte gearChance;

        [ObservableProperty] public GameIndex_Wrapper drop1;
        [ObservableProperty] public GameIndex_Wrapper drop1Rare;
        [ObservableProperty] public GameIndex_Wrapper drop2;
        [ObservableProperty] public GameIndex_Wrapper drop2Rare;
        [ObservableProperty] public byte drop1Count;
        [ObservableProperty] public byte drop1RareCount;
        [ObservableProperty] public byte drop2Count;
        [ObservableProperty] public byte drop2RareCount;

        // Overkills
        [ObservableProperty] public GameIndex_Wrapper dropOverkill1;
        [ObservableProperty] public GameIndex_Wrapper dropOverkill1Rare;
        [ObservableProperty] public GameIndex_Wrapper dropOverkill2;
        [ObservableProperty] public GameIndex_Wrapper dropOverkill2Rare;
        [ObservableProperty] public byte dropOverkillCount;
        [ObservableProperty] public byte dropOverkillRareCount;
        [ObservableProperty] public byte dropOverkill2Count;
        [ObservableProperty] public byte dropOverkill2RareCount;

        // Steals
        [ObservableProperty] public GameIndex_Wrapper steal;
        [ObservableProperty] public GameIndex_Wrapper stealRare;
        [ObservableProperty] public byte stealCount;
        [ObservableProperty] public byte stealRareCount;
        [ObservableProperty] public GameIndex_Wrapper bribe;
        [ObservableProperty] public byte bribeCount;

        // Gear
        [ObservableProperty][NotifyPropertyChangedFor(nameof(NoticeSlots))] public byte gearSlotCount;
        [ObservableProperty] public byte gearFormula;
        [ObservableProperty] public byte gearCrit;
        [ObservableProperty] public byte gearAttack;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(NoticeAbis))] public byte gearAbilityCount;

        [ObservableProperty] public GameIndex_Wrapper[] tidusWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] tidusArmors;
        [ObservableProperty] public GameIndex_Wrapper[] yunaWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] yunaArmors;
        [ObservableProperty] public GameIndex_Wrapper[] auronWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] auronArmors;
        [ObservableProperty] public GameIndex_Wrapper[] kimahriWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] kimahriArmors;
        [ObservableProperty] public GameIndex_Wrapper[] wakkaWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] wakkaArmors;
        [ObservableProperty] public GameIndex_Wrapper[] luluWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] luluArmors;
        [ObservableProperty] public GameIndex_Wrapper[] rikkuWeapons;
        [ObservableProperty] public GameIndex_Wrapper[] rikkuArmors;

        // Extra
        [ObservableProperty] public byte zanmatoLevel;
        [ObservableProperty] public byte unk1;
        [ObservableProperty] public byte unk2;
        [ObservableProperty] public byte unk3;

        public string NoticeSlots => "Slots: ["+ GetRandomSlotsMin(GearSlotCount) +"] - [" + GetRandomSlotsMax(GearSlotCount) +"]";
        public string NoticeAbis => "Slots: ["+ GetRandomAbiMin(GearAbilityCount) +"] - [" + GetRandomAbiMax(GearAbilityCount) +"]";

        public static MonsterLoot_Wrapper Wrap(
            Monster_Loot loot,
            IReadOnlyDictionary<ushort, string>? currentCommandNames = null)
        {
            MonsterLoot_Wrapper wrapper = new();

            ushort noneCommandId = loot.RonsoRageId is 0x0000 or 0x00FF
                ? loot.RonsoRageId
                : (ushort)0x0000;
            IEnumerable<ushort> commandIndexes = CommandCharacter_Dictionary.Instance.Keys;
            if (currentCommandNames is not null)
            {
                commandIndexes = commandIndexes.Concat(
                    currentCommandNames.Keys
                        .Where(commandId => (commandId & 0xF000) == 0x3000)
                        .Select(commandId => (ushort)(commandId & 0x0FFF)));
            }

            List<RonsoRageCommandOption> ronsoRageCommands = [new(noneCommandId, "NONE")];
            foreach (ushort commandIndex in commandIndexes.Distinct().OrderBy(index => index))
            {
                ushort commandId = (ushort)(0x3000 | commandIndex);
                string displayName =
                    currentCommandNames is not null &&
                    currentCommandNames.TryGetValue(commandId, out string? currentName)
                        ? currentName
                        : CommandCharacter_Dictionary.Instance.TryGetValue(commandIndex, out string? fallbackName)
                            ? fallbackName
                            : $"Command {commandIndex}";
                ronsoRageCommands.Add(new(commandId, displayName));
            }

            RonsoRageCommandOption? selectedRonsoRage = ronsoRageCommands
                .FirstOrDefault(option => option.CommandId == loot.RonsoRageId);
            if (selectedRonsoRage is null)
            {
                selectedRonsoRage = new(
                    loot.RonsoRageId,
                    $"Unknown (0x{loot.RonsoRageId:X4})");
                ronsoRageCommands.Insert(1, selectedRonsoRage);
            }

            wrapper.RonsoRageCommands = ronsoRageCommands;
            wrapper.selectedRonsoRage = selectedRonsoRage;

            wrapper.gil = loot.Gil;
            wrapper.ap = loot.Ap;
            wrapper.apOverkill = loot.ApOverkill;

            wrapper.drop1Chance = loot.Drop1Chance;
            wrapper.drop2Chance = loot.Drop2Chance;
            wrapper.stealChance = loot.StealChance;
            wrapper.gearChance = loot.GearChance;

            wrapper.Drop1 = GameIndex_Wrapper.Wrap(loot.Drop1Id);
            wrapper.Drop1Rare = GameIndex_Wrapper.Wrap(loot.Drop1RareId);
            wrapper.Drop2 = GameIndex_Wrapper.Wrap(loot.Drop2Id);
            wrapper.Drop2Rare = GameIndex_Wrapper.Wrap(loot.Drop2RareId);

            wrapper.drop1Count = loot.Drop1Count;
            wrapper.drop1RareCount = loot.Drop1RareCount;
            wrapper.drop2Count = loot.Drop2Count;
            wrapper.drop2RareCount = loot.Drop2RareCount;


            wrapper.DropOverkill1 = GameIndex_Wrapper.Wrap(loot.DropOverkillId);
            wrapper.DropOverkill1Rare = GameIndex_Wrapper.Wrap(loot.DropOverkillRareId);
            wrapper.DropOverkill2 = GameIndex_Wrapper.Wrap(loot.DropOverkill2Id);
            wrapper.DropOverkill2Rare = GameIndex_Wrapper.Wrap(loot.DropOverkill2RareId);

            wrapper.dropOverkillCount = loot.DropOverkillCount;
            wrapper.dropOverkillRareCount = loot.DropOverkillRareCount;
            wrapper.dropOverkill2Count = loot.DropOverkill2Count;
            wrapper.dropOverkill2RareCount = loot.DropOverkill2RareCount;

            wrapper.Steal = GameIndex_Wrapper.Wrap(loot.StealId);
            wrapper.StealRare = GameIndex_Wrapper.Wrap(loot.StealRareId);

            wrapper.stealCount = loot.StealCount;
            wrapper.stealRareCount = loot.StealRareCount;

            wrapper.Bribe = GameIndex_Wrapper.Wrap(loot.BribeId);

            wrapper.bribeCount = loot.BribeCount;

            wrapper.gearSlotCount = loot.GearSlotCount;
            wrapper.gearFormula = loot.GearFormula;
            wrapper.gearCrit = loot.GearCrit;
            wrapper.gearAttack = loot.GearAttack;
            wrapper.gearAbilityCount = loot.GearAbilityCount;

            (wrapper.tidusWeapons, wrapper.tidusArmors) = WrapGearAbilities(loot.TidusAbilities);
            (wrapper.yunaWeapons, wrapper.yunaArmors) = WrapGearAbilities(loot.YunaAbilities);
            (wrapper.auronWeapons, wrapper.auronArmors) = WrapGearAbilities(loot.AuronAbilities);
            (wrapper.kimahriWeapons, wrapper.kimahriArmors) = WrapGearAbilities(loot.KimahriAbilities);
            (wrapper.wakkaWeapons, wrapper.wakkaArmors) = WrapGearAbilities(loot.WakkaAbilities);
            (wrapper.luluWeapons, wrapper.luluArmors) = WrapGearAbilities(loot.LuluAbilities);
            (wrapper.rikkuWeapons, wrapper.rikkuArmors) = WrapGearAbilities(loot.RikkuAbilities);

            wrapper.zanmatoLevel = loot.ZanmatoLevel;
            wrapper.unk1 = loot.Unk1;
            wrapper.unk2 = loot.Unk2;
            wrapper.unk3 = loot.Unk3;

            return wrapper;
        }

        public Monster_Loot Unwrap()
        {
            Monster_Loot loot = new();

            loot.Gil = gil;
            loot.Ap = ap;
            loot.ApOverkill = apOverkill;

            loot.RonsoRageId = SelectedRonsoRage.CommandId;

            loot.Drop1Chance = drop1Chance;
            loot.Drop2Chance = drop2Chance;
            loot.StealChance = stealChance;
            loot.GearChance = gearChance;

            loot.Drop1Id = drop1.Unwrap();
            loot.Drop1RareId = drop1Rare.Unwrap();
            loot.Drop2Id = drop2.Unwrap();
            loot.Drop2RareId = drop2Rare.Unwrap();

            loot.Drop1Count = drop1Count;
            loot.Drop1RareCount = drop1RareCount;
            loot.Drop2Count = drop2Count;
            loot.Drop2RareCount = drop2RareCount;

            loot.DropOverkillId = dropOverkill1.Unwrap();
            loot.DropOverkillRareId = dropOverkill1Rare.Unwrap();
            loot.DropOverkill2Id = dropOverkill2.Unwrap();
            loot.DropOverkill2RareId = dropOverkill2Rare.Unwrap();

            loot.DropOverkillCount = dropOverkillCount;
            loot.DropOverkillRareCount = dropOverkillRareCount;
            loot.DropOverkill2Count = dropOverkill2Count;
            loot.DropOverkill2RareCount = dropOverkill2RareCount;

            loot.StealId = steal.Unwrap();
            loot.StealRareId = stealRare.Unwrap();

            loot.StealCount = stealCount;
            loot.StealRareCount = stealRareCount;

            loot.BribeId = bribe.Unwrap();

            loot.BribeCount = bribeCount;

            loot.GearSlotCount = gearSlotCount;
            loot.GearFormula = gearFormula;
            loot.GearCrit = gearCrit;
            loot.GearAttack = gearAttack;
            loot.GearAbilityCount = gearAbilityCount;

            loot.TidusAbilities = UnwrapGearAbilities(tidusWeapons, tidusArmors);
            loot.YunaAbilities = UnwrapGearAbilities(yunaWeapons, yunaArmors);
            loot.AuronAbilities = UnwrapGearAbilities(auronWeapons, auronArmors);
            loot.KimahriAbilities = UnwrapGearAbilities(kimahriWeapons, kimahriArmors);
            loot.WakkaAbilities = UnwrapGearAbilities(wakkaWeapons, wakkaArmors);
            loot.LuluAbilities = UnwrapGearAbilities(luluWeapons, luluArmors);
            loot.RikkuAbilities = UnwrapGearAbilities(rikkuWeapons, rikkuArmors);

            loot.ZanmatoLevel = zanmatoLevel;
            loot.Unk1 = unk1;
            loot.Unk2 = unk2;
            loot.Unk3 = unk3;

            return loot;
        }

        private static (GameIndex_Wrapper[], GameIndex_Wrapper[]) WrapGearAbilities(LootGearAbilities gearAbilities)
        {
            GameIndex_Wrapper[] weapons = new GameIndex_Wrapper[8];
            GameIndex_Wrapper[] armors = new GameIndex_Wrapper[8];

            for (int i = 0; i < 8; i++)
            {
                weapons[i] = GameIndex_Wrapper.Wrap(gearAbilities.WeaponAbilities[i]);
                armors[i] = GameIndex_Wrapper.Wrap(gearAbilities.ArmorAbilities[i]);
            }

            return (weapons, armors);
        }
        private static LootGearAbilities UnwrapGearAbilities(GameIndex_Wrapper[] weapons, GameIndex_Wrapper[] armors)
        {
            LootGearAbilities gearAbilities = new();

            for (int i = 0; i < 8; i++)
            {
                gearAbilities.WeaponAbilities[i] = weapons[i].Unwrap();
                gearAbilities.ArmorAbilities[i] = armors[i].Unwrap();
            }

            return gearAbilities;
        }

        private static float GetRandomSlotsMin(byte value) => GetRandomValue(value, -4, 4);
        private static float GetRandomSlotsMax(byte value) => GetRandomValue(value, 3, 4);
        private static float GetRandomAbiMin(byte value) => GetRandomValue(value, -4, 8);
        private static float GetRandomAbiMax(byte value) => GetRandomValue(value, 3, 8);
        private static float GetRandomValue(byte value, int variance, int divisor)
        {
            float result = (float)(value + variance) / (float)divisor;
            return result;
        }
    }
}
