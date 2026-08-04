using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record TreasureScanFailure(string Path, string Message);

public sealed record TreasureMapIndex(
    TreasureCatalog Catalog,
    IReadOnlyList<FieldMapAsset> Fields,
    IReadOnlyList<EventTreasureScanResult> EventScans,
    IReadOnlyList<TreasureScanFailure> Failures)
{
    public IReadOnlyList<EventTreasureCandidate> Candidates =>
        EventScans.SelectMany(scan => scan.Candidates).ToArray();

    public IReadOnlyList<EventTreasureCandidate> ConfirmedChestCandidates =>
        Candidates.Where(candidate => candidate.HasChestModel).ToArray();
}

/// <summary>
/// Builds a read-only index directly from an extracted FFX master directory.
/// It never copies or modifies game assets.
/// </summary>
public static class TreasureMapIndexBuilder
{
    public static TreasureMapIndex BuildField(TreasureCatalog catalog, FieldMapAsset field)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(field);
        var scans = new List<EventTreasureScanResult>();
        var failures = new List<TreasureScanFailure>();
        try { _ = Map1Header.Read(field.MapPath); }
        catch (Exception ex) { failures.Add(new TreasureScanFailure(field.MapPath, ex.Message)); }
        foreach (string eventPath in field.EventPaths)
        {
            try { scans.Add(EventTreasureScanner.Scan(eventPath)); }
            catch (Exception ex) { failures.Add(new TreasureScanFailure(eventPath, ex.Message)); }
        }
        return new TreasureMapIndex(catalog, [field], scans, failures);
    }

    public static TreasureMapIndex Build(string masterDirectory, Action<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterDirectory);
        string master = Path.GetFullPath(masterDirectory);
        if (!Directory.Exists(master))
            throw new DirectoryNotFoundException($"FFX master directory was not found: {master}");

        TreasureMapPrerequisiteResult prerequisites = TreasureMapPrerequisites.Validate(master);
        if (!prerequisites.IsValid) throw new InvalidDataException(prerequisites.Message);
        progress?.Invoke("Reading treasure and field indexes…");
        string treasurePath = Path.Combine(master, "jppc", "battle", "kernel", "takara.bin");
        TreasureCatalog catalog = TreasureCatalog.Read(treasurePath);
        IReadOnlyList<FieldMapAsset> fields = FieldAssetDiscovery.ScanMaster(master);
        var scans = new List<EventTreasureScanResult>();
        var failures = new List<TreasureScanFailure>();

        for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            FieldMapAsset field = fields[fieldIndex];
            if (fieldIndex % 20 == 0) progress?.Invoke($"Scanning field events… {fieldIndex:N0} of {fields.Count:N0}");
            try
            {
                _ = Map1Header.Read(field.MapPath);
            }
            catch (Exception ex)
            {
                failures.Add(new TreasureScanFailure(field.MapPath, ex.Message));
            }

            foreach (string eventPath in field.EventPaths)
            {
                try
                {
                    scans.Add(EventTreasureScanner.Scan(eventPath));
                }
                catch (Exception ex)
                {
                    failures.Add(new TreasureScanFailure(eventPath, ex.Message));
                }
            }
        }

        progress?.Invoke("Building projected chest locations…");
        return new TreasureMapIndex(catalog, fields, scans, failures);
    }
}
