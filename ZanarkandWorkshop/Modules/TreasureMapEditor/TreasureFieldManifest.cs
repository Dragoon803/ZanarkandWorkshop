using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

/// <summary>
/// Lightweight vanilla field metadata used to populate the selector without parsing every event.
/// Selected fields are always reparsed from the user's project before display or editing.
/// </summary>
internal static class TreasureFieldManifest
{
    private static readonly IReadOnlyDictionary<string, int> KnownChestCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["azit03"] = 4, ["azit04"] = 1, ["azit06"] = 2, ["azit07"] = 2,
            ["bika00"] = 2, ["bika01"] = 3, ["bika02"] = 11, ["bika03"] = 8,
            ["bjyt00"] = 2, ["bjyt02"] = 2, ["bjyt04"] = 2, ["bjyt06"] = 1, ["bjyt10"] = 2,
            ["bsil00"] = 2, ["bsil01"] = 2, ["bsil03"] = 3, ["bsil04"] = 2, ["bsil05"] = 1,
            ["bsvr00"] = 8, ["bsyt01"] = 1, ["bsyt06"] = 4, ["bvyt09"] = 7, ["bvyt11"] = 2,
            ["djyt01"] = 2, ["djyt02"] = 1, ["djyt03"] = 1, ["djyt04"] = 1,
            ["djyt05"] = 1, ["djyt06"] = 1, ["djyt09"] = 2,
            ["dome00"] = 3, ["dome01"] = 1, ["dome06"] = 1, ["dome07"] = 1,
            ["genk00"] = 4, ["genk04"] = 1, ["genk06"] = 1, ["genk11"] = 1,
            ["genk15"] = 1, ["genk16"] = 1,
            ["guad00"] = 2, ["guad04"] = 1, ["guad05"] = 1,
            ["ikai00"] = 1, ["ikai06"] = 1, ["ikai08"] = 1,
            ["kami00"] = 5, ["kami03"] = 4, ["kami04"] = 1,
            ["kino00"] = 1, ["kino01"] = 4, ["kino02"] = 2, ["kino04"] = 2,
            ["kino05"] = 1, ["kino07"] = 1, ["kino09"] = 1,
            ["klyt00"] = 3, ["klyt10"] = 1, ["klyt12"] = 3,
            ["lchb01"] = 2, ["lchb02"] = 1, ["lchb05"] = 2, ["lchb14"] = 1,
            ["luca06"] = 1, ["maca00"] = 1, ["maca03"] = 2, ["maca04"] = 4,
            ["mcfr00"] = 2, ["mcfr01"] = 2, ["mcfr02"] = 2, ["mcfr09"] = 1,
            ["mcyt02"] = 2, ["mcyt03"] = 1, ["mcyt04"] = 1, ["mcyt05"] = 1, ["mcyt07"] = 2,
            ["mihn00"] = 1, ["mihn02"] = 1, ["mihn04"] = 2, ["mihn05"] = 3,
            ["mihn06"] = 1, ["mihn07"] = 1, ["mihn08"] = 2,
            ["mtgz01"] = 5, ["mtgz02"] = 1, ["mtgz06"] = 1, ["mtgz07"] = 4,
            ["nagi00"] = 3, ["nagi05"] = 7, ["nagi07"] = 1,
            ["omeg00"] = 13, ["omeg01"] = 2,
            ["ptkl08"] = 1, ["ptkl17"] = 1, ["ptkl18"] = 1,
            ["sins02"] = 5, ["sins04"] = 8, ["slik07"] = 1, ["stbv00"] = 2,
            ["swin07"] = 1, ["zkrn02"] = 2,
        };

    public static bool TryGetChestCount(string fieldId, out int count) => KnownChestCounts.TryGetValue(fieldId, out count);
}
