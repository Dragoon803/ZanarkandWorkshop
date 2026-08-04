using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record TreasureRewardOption(ushort EncodedId, string Display)
{
    public override string ToString() => Display;
}

public static class TreasureRewardLookup
{
    private static readonly Dictionary<(string Master, TreasureKind Kind), IReadOnlyList<TreasureRewardOption>> Cache = [];
    private static readonly string[] KeyItems =
    {
        "Withered Bouquet", "Flint", "Cloudy Mirror", "Celestial Mirror",
        "Al Bhed Primer I", "Al Bhed Primer II", "Al Bhed Primer III", "Al Bhed Primer IV",
        "Al Bhed Primer V", "Al Bhed Primer VI", "Al Bhed Primer VII", "Al Bhed Primer VIII",
        "Al Bhed Primer IX", "Al Bhed Primer X", "Al Bhed Primer XI", "Al Bhed Primer XII",
        "Al Bhed Primer XIII", "Al Bhed Primer XIV", "Al Bhed Primer XV", "Al Bhed Primer XVI",
        "Al Bhed Primer XVII", "Al Bhed Primer XVIII", "Al Bhed Primer XIX", "Al Bhed Primer XX",
        "Al Bhed Primer XXI", "Al Bhed Primer XXII", "Al Bhed Primer XXIII", "Al Bhed Primer XXIV",
        "Al Bhed Primer XXV", "Al Bhed Primer XXVI", "Summoner's Soul", "Aeon's Soul",
        "Jecht's Sphere", "Rusty Sword", "Unknown Key Item 34", "Sun Crest", "Sun Sigil",
        "Moon Crest", "Moon Sigil", "Mars Crest", "Mars Sigil", "Mark of Conquest",
        "Saturn Crest", "Saturn Sigil", "Jupiter Crest", "Jupiter Sigil", "Venus Crest",
        "Venus Sigil", "Mercury Crest", "Mercury Sigil", "Blossom Crown", "Flower Scepter"
    };

    public static IReadOnlyList<TreasureRewardOption> Build(TreasureKind kind, string masterPath)
    {
        string master = Path.GetFullPath(masterPath);
        lock (Cache)
        {
            if (Cache.TryGetValue((master, kind), out IReadOnlyList<TreasureRewardOption>? existing)) return existing;
            IReadOnlyList<TreasureRewardOption> created = kind switch
            {
                TreasureKind.Gil => [new TreasureRewardOption(0, "Gil")],
                TreasureKind.Item => Item_Dictionary.Instance.OrderBy(pair => pair.Key)
                    .Select(pair => new TreasureRewardOption((ushort)(0x2000 + pair.Key), pair.Value)).ToArray(),
                TreasureKind.KeyItem => KeyItems.Select((name, index) =>
                    new TreasureRewardOption((ushort)(0xA000 + index), name)).ToArray(),
                TreasureKind.Equipment => ReadGearOptions(Path.Combine(master, "jppc", "battle", "kernel", "buki_get.bin")),
                _ => []
            };
            Cache[(master, kind)] = created;
            return created;
        }
    }

    public static string Describe(TreasureKind kind, byte quantity, ushort encodedId, string masterPath)
    {
        if (kind == TreasureKind.Gil) return $"{quantity * 100:N0} Gil";
        TreasureRewardOption? option = Build(kind, masterPath).FirstOrDefault(value => value.EncodedId == encodedId);
        return option is null ? $"Unknown / modded ID 0x{encodedId:X4}" :
            kind == TreasureKind.Equipment ? option.Display : $"{quantity} × {option.Display}";
    }

    private static IReadOnlyList<TreasureRewardOption> ReadGearOptions(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 0x14) throw new InvalidDataException("buki_get.bin is shorter than its header.");
        int count = BitConverter.ToUInt16(bytes, 0x0A) + 1;
        int start = checked((int)BitConverter.ToUInt32(bytes, 0x10));
        if (start < 0x14 || start + count * 16 > bytes.Length)
            throw new InvalidDataException("buki_get.bin has an invalid record table.");
        var options = new List<TreasureRewardOption>(count);
        for (int id = 0; id < count; id++)
        {
            int offset = start + id * 16;
            int owner = bytes[offset + 1];
            string ownerName = Enum.IsDefined(typeof(Character_Enum), (sbyte)owner) ? ((Character_Enum)(sbyte)owner).ToString() : $"Character {owner}";
            string gearType = bytes[offset + 2] == 0 ? "Weapon" : bytes[offset + 2] == 1 ? "Armor" : $"Gear type {bytes[offset + 2]}";
            int slots = bytes[offset + 7];
            string[] abilities = Enumerable.Range(0, 4).Select(index => BitConverter.ToUInt16(bytes, offset + 8 + index * 2))
                .Where(raw => raw != 0x00FF).Select(raw =>
                {
                    ushort abilityId = raw >= 0x8000 ? (ushort)(raw - 0x8000) : raw;
                    return AutoAbility_Dictionary.Instance.TryGetValue(abilityId, out string? name) ? name : $"Ability 0x{raw:X4}";
                }).ToArray();
            string abilityText = abilities.Length == 0 ? "No abilities" : string.Join(", ", abilities);
            options.Add(new TreasureRewardOption((ushort)id, $"#{id} · {ownerName} {gearType} · {slots} slot{(slots == 1 ? "" : "s")} · {abilityText}"));
        }
        return options;
    }
}
