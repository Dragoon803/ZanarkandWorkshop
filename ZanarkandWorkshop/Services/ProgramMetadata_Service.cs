using System;
using System.Diagnostics;
using System.IO;

namespace FFXProjectEditor.Services;

/// <summary>
/// Provides the standard location for Zanarkand Workshop-owned metadata and
/// migrates preferences from the legacy Local AppData folder.
/// </summary>
public static class ProgramMetadata_Service
{
    private const string MetadataRootOverrideVariable = "ZANARKAND_WORKSHOP_METADATA_ROOT";
    private static readonly string[] KnownMetadataFiles =
    {
        "ai-editor-preferences.json",
        "recent-projects.txt",
        "settings.json",
        "sphere-grid-editor-preferences.json",
        "vanilla-master.txt",
        "window-size.txt"
    };

    private static readonly string LegacyRootPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor");

    public static string RootPath { get; } = InitializeRootPath();

    public static void EnsureDirectory() => Directory.CreateDirectory(RootPath);

    private static string InitializeRootPath()
    {
        // Enables destructive recovery tests to run against an isolated directory.
        // Normal application launches do not define this variable.
        string? overriddenRoot = Environment.GetEnvironmentVariable(MetadataRootOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overriddenRoot))
        {
            string isolatedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(overriddenRoot));
            Directory.CreateDirectory(isolatedRoot);
            return isolatedRoot;
        }
        string stableRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZanarkandWorkshop");
        Directory.CreateDirectory(stableRoot);

        // Earlier builds stored global state beside the executable. Import each
        // artifact without overwriting anything already present in the stable root.
        // This must be a one-time import: repeatedly filling missing directories
        // would resurrect projects that the user deliberately removed from the list.
        string executableRoot = Path.Combine(AppContext.BaseDirectory, "metadata");
        string migrationMarker = Path.Combine(stableRoot, ".executable-metadata-migrated-v1");
        if (!string.Equals(Path.GetFullPath(executableRoot), Path.GetFullPath(stableRoot),
                StringComparison.OrdinalIgnoreCase) && !File.Exists(migrationMarker))
        {
            try
            {
                // An existing stable registry means a previous version already
                // completed this import before the marker was introduced.
                if (!File.Exists(Path.Combine(stableRoot, "projects.json")) &&
                    Directory.Exists(executableRoot))
                    CopyMissing(executableRoot, stableRoot);
                File.WriteAllText(migrationMarker, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex) { Debug.WriteLine($"Could not migrate executable metadata: {ex.Message}"); }
        }
        return stableRoot;
    }

    private static void CopyMissing(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
    }

    public static void MigrateKnownFiles()
    {
        EnsureDirectory();
        foreach (string fileName in KnownMetadataFiles)
            _ = GetFilePath(fileName);
    }

    public static string GetFilePath(string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            throw new ArgumentException("Metadata file names cannot contain a path.", nameof(fileName));

        string destination = Path.Combine(RootPath, fileName);
        TryMigrateLegacyFile(fileName, destination);
        return destination;
    }

    private static void TryMigrateLegacyFile(string fileName, string destination)
    {
        string legacyPath = Path.Combine(LegacyRootPath, fileName);
        if (!File.Exists(legacyPath))
            return;

        try
        {
            if (!File.Exists(destination))
            {
                EnsureDirectory();
                File.Move(legacyPath, destination);
            }
            else
            {
                // The centralized metadata copy is authoritative after migration.
                File.Delete(legacyPath);
            }
        }
        catch (Exception ex)
        {
            // Preference migration must never prevent Zanarkand Workshop from opening.
            Debug.WriteLine($"Could not migrate metadata file '{fileName}': {ex.Message}");
        }
    }
}
