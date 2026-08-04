using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Battlefield;

public sealed record BattlefieldAsset(ushort Id, string Code, string MapPath);

public static class BattlefieldAssetCatalog
{
    private static readonly (ushort Id, string Code)[] Known =
    {
        (0x401, "grid00_a"), (0x402, "cdsp00_a"),
        (0x403, "bsil03_a"), (0x404, "bsil07_a"), (0x405, "bsil05_a"),
        (0x406, "klyt00_a"), (0x407, "mihn00_a"), (0x408, "mihn00_b"),
        (0x409, "mihn04_a"), (0x40A, "mihn04_b"), (0x40B, "kino00_a"),
        (0x40C, "kino01_a"), (0x40D, "kino05_a"), (0x40E, "kino07_a"),
        (0x40F, "kino04_a"), (0x410, "genk00_a"), (0x411, "genk16_a"),
        (0x412, "kami00_a"), (0x413, "kami03_a"), (0x414, "mcfr00_a"),
        (0x415, "maca00_a"), (0x416, "mcyt00_a"), (0x417, "maca03_a"),
        (0x418, "bika00_a"), (0x419, "bika01_a"), (0x41A, "bika02_b"),
        (0x41B, "bika03_b"), (0x41C, "bika03_c"), (0x41D, "bika04_a"),
        (0x41E, "azit03_a"), (0x41F, "hiku02_a"), (0x420, "bvyt00_a"),
        (0x421, "bvyt09_a"), (0x422, "bvyt09_b"), (0x423, "stbv00_a"),
        (0x424, "stbv01_a"), (0x425, "nagi00_a"), (0x426, "nagi00_b"),
        (0x427, "lmyt01_a"), (0x428, "nagi03_a"), (0x429, "nagi04_a"),
        (0x42A, "nagi05_a"), (0x42B, "nagi05_b"), (0x42C, "mtgz01_a"),
        (0x42D, "mtgz06_a"), (0x42E, "mtgz07_a"), (0x42F, "zkrn02_a"),
        (0x430, "dome00_a"), (0x431, "sins02_a"), (0x432, "sins04_a"),
        (0x433, "sins05_a"), (0x434, "omeg00_a"), (0x435, "omeg01_a"),
        (0x436, "test00_a"), (0x437, "test00_b"), (0x438, "nagi05_c"),
        (0x439, "sfia00_a")
    };

    public static IReadOnlyList<BattlefieldAsset> Discover(string projectRoot)
    {
        string root = Path.Combine(projectRoot, "jppc", "btlmap");
        if (!Directory.Exists(root)) return [];

        var paths = Directory.EnumerateFiles(root, "mapout.vpa", SearchOption.AllDirectories)
            .ToDictionary(
                path => new DirectoryInfo(Path.GetDirectoryName(Path.GetDirectoryName(path))!).Name,
                Path.GetFullPath,
                StringComparer.OrdinalIgnoreCase);

        return Known
            .Where(entry => paths.ContainsKey(entry.Code))
            .Select(entry => new BattlefieldAsset(entry.Id, entry.Code, paths[entry.Code]))
            .ToArray();
    }

    public static IReadOnlyList<BattlefieldAsset> MatchFormation(
        string formationName,
        IReadOnlyList<BattlefieldAsset> assets)
    {
        string name = Path.GetFileNameWithoutExtension(formationName).ToLowerInvariant();
        string stem = name.Length >= 6 ? name[..6] : name;
        return assets.Where(asset => asset.Code.StartsWith(stem, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
