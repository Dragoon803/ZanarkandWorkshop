using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public enum SphereGridNodeCategory
{
    Lock,
    Empty,
    Attribute,
    Skill,
    Special,
    WhiteMagic,
    BlackMagic,
    Unknown
}

public sealed record SphereGridNodeTypeInfo(
    byte Id,
    string Name,
    string ShortName,
    SphereGridNodeCategory Category,
    bool IsKnown = true);

public static class SphereGridNodeTypes
{
    private static readonly IReadOnlyDictionary<byte, SphereGridNodeTypeInfo> Known =
        BuildKnown();
    private static readonly IReadOnlyList<SphereGridNodeTypeInfo> KnownList =
        new List<SphereGridNodeTypeInfo>(Known.Values);

    public static IReadOnlyList<SphereGridNodeTypeInfo> All => KnownList;

    public static SphereGridNodeTypeInfo Get(byte id) =>
        Known.TryGetValue(id, out SphereGridNodeTypeInfo? info)
            ? info
            : new SphereGridNodeTypeInfo(
                id, $"Unknown 0x{id:X2}", $"0x{id:X2}",
                SphereGridNodeCategory.Unknown, false);

    private static IReadOnlyDictionary<byte, SphereGridNodeTypeInfo> BuildKnown()
    {
        var result = new Dictionary<byte, SphereGridNodeTypeInfo>();

        Add(0x00, "Level 3 Lock", "Lock3", SphereGridNodeCategory.Lock);
        Add(0x01, "Empty", "Empty", SphereGridNodeCategory.Empty);

        string[] attributes =
        {
            "Strength", "Defense", "Magic", "Magic Defense",
            "Agility", "Luck", "Evasion", "Accuracy"
        };
        string[] abbreviations =
        {
            "Str", "Def", "Mag", "MDef", "Agi", "Lck", "Eva", "Acc"
        };
        byte id = 0x02;
        for (int attribute = 0; attribute < attributes.Length; attribute++)
        {
            for (int amount = 1; amount <= 4; amount++)
            {
                Add(id++, $"{attributes[attribute]} +{amount}",
                    $"{abbreviations[attribute]}+{amount}",
                    SphereGridNodeCategory.Attribute);
            }
        }

        Add(0x22, "HP +200", "HP+200", SphereGridNodeCategory.Attribute);
        Add(0x23, "HP +300", "HP+300", SphereGridNodeCategory.Attribute);
        Add(0x24, "MP +40", "MP+40", SphereGridNodeCategory.Attribute);
        Add(0x25, "MP +20", "MP+20", SphereGridNodeCategory.Attribute);
        Add(0x26, "MP +10", "MP+10", SphereGridNodeCategory.Attribute);
        Add(0x27, "Level 1 Lock", "Lock1", SphereGridNodeCategory.Lock);
        Add(0x28, "Level 2 Lock", "Lock2", SphereGridNodeCategory.Lock);
        Add(0x29, "Level 4 Lock", "Lock4", SphereGridNodeCategory.Lock);

        AddRange(0x2A, SphereGridNodeCategory.Skill,
            ("Delay Attack", "Delay Atk"), ("Delay Buster", "Delay Bst"),
            ("Sleep Attack", "Sleep Atk"), ("Silence Attack", "Silence Atk"),
            ("Dark Attack", "Dark Atk"), ("Zombie Attack", "Zombie Atk"),
            ("Sleep Buster", "Sleep Bst"), ("Silence Buster", "Silence Bst"),
            ("Dark Buster", "Dark Bst"), ("Triple Foul", "Triple Foul"),
            ("Power Break", "Power Brk"), ("Magic Break", "Magic Brk"),
            ("Armor Break", "Armor Brk"), ("Mental Break", "Mental Brk"),
            ("Mug", "Mug"), ("Quick Hit", "Quick Hit"));

        AddRange(0x3A, SphereGridNodeCategory.Special,
            ("Steal", "Steal"), ("Use", "Use"), ("Flee", "Flee"),
            ("Pray", "Pray"), ("Cheer", "Cheer"), ("Focus", "Focus"),
            ("Reflex", "Reflex"), ("Aim", "Aim"), ("Luck", "Luck"),
            ("Jinx", "Jinx"), ("Lancet", "Lancet"), ("Guard", "Guard"),
            ("Sentinel", "Sentinel"), ("Spare Change", "Spare Chg"),
            ("Threaten", "Threaten"), ("Provoke", "Provoke"),
            ("Entrust", "Entrust"), ("Copycat", "Copycat"),
            ("Doublecast", "Doublecast"), ("Bribe", "Bribe"));

        AddRange(0x4E, SphereGridNodeCategory.WhiteMagic,
            ("Cure", "Cure"), ("Cura", "Cura"), ("Curaga", "Curaga"),
            ("NulFrost", "NulFrost"), ("NulBlaze", "NulBlaze"),
            ("NulShock", "NulShock"), ("NulTide", "NulTide"),
            ("Scan", "Scan"), ("Esuna", "Esuna"), ("Life", "Life"),
            ("Full-Life", "Full-Life"), ("Haste", "Haste"),
            ("Hastega", "Hastega"), ("Slow", "Slow"), ("Slowga", "Slowga"),
            ("Shell", "Shell"), ("Protect", "Protect"), ("Reflect", "Reflect"),
            ("Dispel", "Dispel"), ("Regen", "Regen"), ("Holy", "Holy"),
            ("Auto-Life", "Auto-Life"));

        AddRange(0x64, SphereGridNodeCategory.BlackMagic,
            ("Blizzard", "Blizzard"), ("Fire", "Fire"), ("Thunder", "Thunder"),
            ("Water", "Water"), ("Fira", "Fira"), ("Blizzara", "Blizzara"),
            ("Thundara", "Thundara"), ("Watera", "Watera"),
            ("Firaga", "Firaga"), ("Blizzaga", "Blizzaga"),
            ("Thundaga", "Thundaga"), ("Waterga", "Waterga"),
            ("Bio", "Bio"), ("Demi", "Demi"), ("Death", "Death"),
            ("Drain", "Drain"), ("Osmose", "Osmose"), ("Flare", "Flare"),
            ("Ultima", "Ultima"));

        AddRange(0x77, SphereGridNodeCategory.Special,
            ("Pilfer Gil", "Pilfer Gil"), ("Full Break", "Full Break"));
        AddRange(0x79, SphereGridNodeCategory.Skill,
            ("Extract Power", "Extract Pwr"), ("Extract Mana", "Extract Mana"),
            ("Extract Speed", "Extract Spd"), ("Extract Ability", "Extract Abl"));
        AddRange(0x7D, SphereGridNodeCategory.Special,
            ("Nab Gil", "Nab Gil"), ("Quick Pockets", "Quick Pockets"));

        return result;

        void Add(
            byte value, string name, string shortName,
            SphereGridNodeCategory category) =>
            result.Add(value, new SphereGridNodeTypeInfo(
                value, name, shortName, category));

        void AddRange(
            byte start, SphereGridNodeCategory category,
            params (string Name, string ShortName)[] entries)
        {
            for (int index = 0; index < entries.Length; index++)
            {
                (string name, string shortName) = entries[index];
                Add(checked((byte)(start + index)), name, shortName, category);
            }
        }
    }
}
