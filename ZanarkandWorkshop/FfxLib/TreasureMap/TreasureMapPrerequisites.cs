using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record TreasureMapPrerequisiteResult(IReadOnlyList<string> MissingPaths)
{
    public bool IsValid => MissingPaths.Count == 0;
    public string Message => IsValid ? "Treasure Map source data is available." :
        "The Treasure Map Editor needs additional source data in the opened master folder:" +
        Environment.NewLine + Environment.NewLine +
        string.Join(Environment.NewLine, MissingPaths.Select(path => "• " + path)) +
        Environment.NewLine + Environment.NewLine +
        "These files are used to identify chests and reconstruct maps. Only takara.bin is deployed for chest-content-only mods.";
}

public static class TreasureMapPrerequisites
{
    public static TreasureMapPrerequisiteResult Validate(string masterPath)
    {
        string root = Path.GetFullPath(masterPath);
        var missing = new List<string>();
        RequireFile(Path.Combine(root, "jppc", "battle", "kernel", "takara.bin"), "jppc\\battle\\kernel\\takara.bin", missing);
        RequireFile(Path.Combine(root, "jppc", "battle", "kernel", "buki_get.bin"), "jppc\\battle\\kernel\\buki_get.bin", missing);
        RequireFolderWith(Path.Combine(root, "jppc", "map"), "mapout.vpa", "jppc\\map (with mapout.vpa files)", missing);
        RequireFolderWith(Path.Combine(root, "jppc", "event", "obj"), "*.ebp", "jppc\\event\\obj (with .ebp event files)", missing);
        return new TreasureMapPrerequisiteResult(missing);
    }

    private static void RequireFile(string path, string display, List<string> missing)
    { if (!File.Exists(path)) missing.Add(display); }

    private static void RequireFolderWith(string path, string pattern, string display, List<string> missing)
    {
        if (!Directory.Exists(path) || !Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories).Any()) missing.Add(display);
    }
}
