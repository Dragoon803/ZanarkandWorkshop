using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record FieldMapAsset(
    string FieldId,
    string AreaId,
    string MapPath,
    IReadOnlyList<string> EventPaths)
{
    public bool HasEvents => EventPaths.Count > 0;
}

public static class FieldAssetDiscovery
{
    public static IReadOnlyList<FieldMapAsset> ScanMaster(string masterPath)
    {
        string root = System.IO.Path.GetFullPath(masterPath);
        string mapRoot = System.IO.Path.Combine(root, "jppc", "map");
        string eventRoot = System.IO.Path.Combine(root, "jppc", "event", "obj");
        if (!Directory.Exists(mapRoot))
            throw new DirectoryNotFoundException($"Missing field-map folder: {mapRoot}");
        if (!Directory.Exists(eventRoot))
            throw new DirectoryNotFoundException($"Missing field-event folder: {eventRoot}");

        Dictionary<string, string[]> eventsByField = Directory
            .EnumerateFiles(eventRoot, "*.ebp", SearchOption.AllDirectories)
            .Where(path => !System.IO.Path.GetFileName(path).StartsWith("cn_", StringComparison.OrdinalIgnoreCase) &&
                           !System.IO.Path.GetFileName(path).StartsWith("psv", StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => ToFieldId(System.IO.Path.GetFileNameWithoutExtension(path)),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length == 6)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(mapRoot, "mapout.vpa", SearchOption.AllDirectories)
            .Select(path =>
            {
                DirectoryInfo? fieldDirectory = Directory.GetParent(System.IO.Path.GetDirectoryName(path)!);
                string fieldId = fieldDirectory?.Name ?? "";
                string areaId = fieldDirectory?.Parent?.Name ?? "";
                eventsByField.TryGetValue(fieldId, out string[]? eventPaths);
                return new FieldMapAsset(fieldId, areaId, System.IO.Path.GetFullPath(path), eventPaths ?? []);
            })
            .Where(asset => asset.FieldId.Length == 6)
            .OrderBy(asset => asset.FieldId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ToFieldId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return "";
        string normalized = eventId.Trim().ToLowerInvariant();
        return normalized.Length >= 6 ? normalized[..6] : normalized;
    }
}
