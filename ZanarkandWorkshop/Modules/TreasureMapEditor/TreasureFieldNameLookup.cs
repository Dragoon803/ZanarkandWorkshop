using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

internal static class TreasureFieldNameLookup
{
    private static readonly IReadOnlyDictionary<string, (string Region, string Location)> ExactNames =
        new Dictionary<string, (string Region, string Location)>(StringComparer.OrdinalIgnoreCase)
        {
            ["bika0200"] = ("Bikanel Desert", "Sanubia Desert - Central"),
            ["bjyt0400"] = ("Baaj Temple", "Ruins - Hall"),
            ["kami0000"] = ("Thunder Plains", "Thunder Plains - South"),
            ["kami0300"] = ("Thunder Plains", "Thunder Plains - North"),
            ["kino0100"] = ("Mushroom Rock", "Mushroom Rock - Valley"),
            ["mcfr0100"] = ("Macalania Forest", "Macalania Forest - Central"),
            ["mcyt0200"] = ("Macalania Temple", "Macalania Temple - Hall"),
            ["nagi0500"] = ("Cavern of the Stolen Fayth", "Cavern of the Stolen Fayth"),
        };

    private static readonly IReadOnlyDictionary<string, string> RegionNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["azit"] = "Al Bhed Home", ["bika"] = "Bikanel Desert",
            ["biyt"] = "Baaj Temple", ["bjyt"] = "Baaj Temple",
            ["blit"] = "Blitzball", ["bltz"] = "Blitzball",
            ["bsil"] = "Besaid Island", ["bsmm"] = "Besaid Beach (Flashback)",
            ["bsvr"] = "Besaid Village", ["bsyt"] = "Besaid Temple", ["bvyt"] = "Besaid Temple",
            ["cdsp"] = "Al Bhed Boat / Underwater Ruins", ["djyt"] = "Djose Temple",
            ["dome"] = "Zanarkand Dome", ["drea"] = "Unknown (dream)",
            ["genk"] = "Moonflow", ["grid"] = "Sphere Grid Plane", ["guad"] = "Guadosalam",
            ["hiku"] = "Airship / World Map", ["ikai"] = "Farplane", ["kami"] = "Thunder Plains",
            ["kino"] = "Mushroom Rock", ["klyt"] = "Kilika Woods / Temple",
            ["ichb"] = "Luca", ["lchb"] = "Luca", ["lchi"] = "Luca",
            ["imyt"] = "Remiem Temple", ["luca"] = "Luca Square",
            ["maca"] = "Lake Macalania", ["mcfr"] = "Macalania Forest", ["mcyt"] = "Macalania Temple",
            ["mihn"] = "Mi'ihen Highroad", ["mmmc"] = "Unknown (mmmc)",
            ["msmm"] = "Via Purifico (Maze)", ["mtgz"] = "Mt. Gagazet / Caves / Upper Zanarkand",
            ["nagi"] = "Calm Lands / Cavern of the Stolen Fayth", ["omeg"] = "Omega Ruins",
            ["ptkl"] = "Kilika Town", ["sins"] = "Inside Sin", ["slik"] = "S.S. Liki",
            ["ssbt"] = "Airship Model", ["stbv"] = "Bevelle Highbridge / Via Purifico",
            ["swin"] = "S.S. Winno", ["titl"] = "Main Menu", ["zkrn"] = "Zanarkand Ruins",
            ["znkd"] = "Dream Zanarkand",
        };

    public static string GetDisplayName(string fieldId, string areaId)
    {
        if (ExactNames.TryGetValue(fieldId, out (string Region, string Location) exact))
            return exact.Region.Equals(exact.Location, StringComparison.OrdinalIgnoreCase)
                ? exact.Location
                : $"{exact.Region} - {TrimRepeatedRegion(exact.Region, exact.Location)}";

        string prefix = fieldId.Length >= 4 ? fieldId[..4] : areaId;
        if (fieldId.StartsWith("blitz", StringComparison.OrdinalIgnoreCase)) prefix = "blit";
        else if (fieldId.StartsWith("dream", StringComparison.OrdinalIgnoreCase)) prefix = "drea";
        return RegionNames.TryGetValue(prefix, out string? region) ? region : areaId;
    }

    private static string TrimRepeatedRegion(string region, string location)
    {
        string prefix = region + " - ";
        return location.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? location[prefix.Length..]
            : location;
    }
}
