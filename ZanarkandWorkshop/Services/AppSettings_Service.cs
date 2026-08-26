using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FFXProjectEditor.Services;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public WindowSettings Window { get; set; } = new();
    public OriginalGameFilesSettings OriginalGameFiles { get; set; } = new();
    public EditorSettings Editors { get; set; } = new();
}

public sealed class WindowSettings
{
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 720;
}

public sealed class OriginalGameFilesSettings
{
    public string? MasterPath { get; set; }
}

public sealed class EditorSettings
{
    public MonsterAiSettings MonsterAi { get; set; } = new();
    public SphereGridSettings SphereGrid { get; set; } = new();
}

public sealed class MonsterAiSettings
{
    public bool HideStatements { get; set; }
    public bool HideInstructions { get; set; }
    public bool SuppressDeleteStatementWarning { get; set; }
    public bool SuppressUnsafePasteWarning { get; set; }
}

public sealed class SphereGridSettings
{
    public bool CreationCautionDismissed { get; set; }
}

/// <summary>Loads and atomically persists global Zanarkand Workshop preferences.</summary>
public static class AppSettings_Service
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string SettingsPath =
        ProgramMetadata_Service.GetFilePath("settings.json");

    public static AppSettings Current { get; } = Load();

    public static void Save()
    {
        lock (Sync)
        {
            ProgramMetadata_Service.EnsureDirectory();
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temporaryPath, SettingsPath, true);
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded is not null)
                {
                    Normalize(loaded);
                    DeleteLegacyFiles();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read settings.json: {ex.Message}");
        }

        AppSettings migrated = MigrateLegacySettings();
        try
        {
            SaveDocument(migrated);
            DeleteLegacyFiles();
        }
        catch (Exception ex) { Debug.WriteLine($"Could not migrate settings.json: {ex.Message}"); }
        return migrated;
    }

    private static AppSettings MigrateLegacySettings()
    {
        var settings = new AppSettings();
        TryMigrateWindow(settings);
        TryMigrateVanillaPath(settings);
        TryMigrateMonsterAi(settings);
        TryMigrateSphereGrid(settings);
        return settings;
    }

    private static void TryMigrateWindow(AppSettings settings)
    {
        try
        {
            string path = ProgramMetadata_Service.GetFilePath("window-size.txt");
            if (!File.Exists(path)) return;
            string[] values = File.ReadAllText(path).Split('x', StringSplitOptions.TrimEntries);
            if (values.Length == 2 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height) &&
                width >= 640 && height >= 480)
            {
                settings.Window.Width = width;
                settings.Window.Height = height;
            }
        }
        catch { }
    }

    private static void TryMigrateVanillaPath(AppSettings settings)
    {
        try
        {
            string path = ProgramMetadata_Service.GetFilePath("vanilla-master.txt");
            if (File.Exists(path))
                settings.OriginalGameFiles.MasterPath = File.ReadAllText(path).Trim();
        }
        catch { }
    }

    private static void TryMigrateMonsterAi(AppSettings settings)
    {
        try
        {
            string path = ProgramMetadata_Service.GetFilePath("ai-editor-preferences.json");
            if (!File.Exists(path)) return;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            settings.Editors.MonsterAi.HideStatements = GetBool(root, "HideGroupedLogic");
            settings.Editors.MonsterAi.HideInstructions = GetBool(root, "HideDecodedInstructions");
            settings.Editors.MonsterAi.SuppressDeleteStatementWarning = GetBool(root, "SuppressDeleteStatementWarning");
            settings.Editors.MonsterAi.SuppressUnsafePasteWarning = GetBool(root, "SuppressUnsafePasteWarning");
        }
        catch { }
    }

    private static void TryMigrateSphereGrid(AppSettings settings)
    {
        try
        {
            string path = ProgramMetadata_Service.GetFilePath("sphere-grid-editor-preferences.json");
            if (!File.Exists(path)) return;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            settings.Editors.SphereGrid.CreationCautionDismissed =
                GetBool(document.RootElement, "SuppressCreationCaution");
        }
        catch { }
    }

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static void Normalize(AppSettings settings)
    {
        settings.Window ??= new WindowSettings();
        settings.OriginalGameFiles ??= new OriginalGameFilesSettings();
        settings.Editors ??= new EditorSettings();
        settings.Editors.MonsterAi ??= new MonsterAiSettings();
        settings.Editors.SphereGrid ??= new SphereGridSettings();
        if (settings.Window.Width < 640) settings.Window.Width = 1280;
        if (settings.Window.Height < 480) settings.Window.Height = 720;
    }

    private static void SaveDocument(AppSettings settings)
    {
        ProgramMetadata_Service.EnsureDirectory();
        string temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static void DeleteLegacyFiles()
    {
        string[] names =
        [
            "window-size.txt",
            "vanilla-master.txt",
            "ai-editor-preferences.json",
            "sphere-grid-editor-preferences.json"
        ];
        foreach (string name in names)
        {
            try
            {
                string path = ProgramMetadata_Service.GetFilePath(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
