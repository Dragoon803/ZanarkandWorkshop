using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.SphereGrid;

/// <summary>
/// Persists editor-only Sphere Grid section colors. These colors are not part of
/// the game's dat files, so Zanarkand Workshop keeps them below the program's
/// dedicated metadata folder.
/// </summary>
public static class SphereGridColorMetadata
{
    public const string FileName = "sphere-grid-colors.zwmeta.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyDictionary<int, SphereGridCharacter> Load(
        string directory, SphereGridKind kind)
    {
        try
        {
            MetadataDocument document = Read(directory);
            if (document.Grids is not null &&
                document.Grids.TryGetValue(kind, out Dictionary<int, SphereGridCharacter>? colors))
            {
                return colors
                    .Where(pair => pair.Key >= 0 && pair.Value != SphereGridCharacter.Unassigned)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return new Dictionary<int, SphereGridCharacter>();
    }

    public static void Save(
        string directory,
        SphereGridKind kind,
        IReadOnlyDictionary<int, SphereGridCharacter> colors)
    {
        Directory.CreateDirectory(directory);
        MetadataDocument document;
        try
        {
            document = Read(directory);
        }
        catch (JsonException)
        {
            document = new MetadataDocument();
        }

        document.Grids ??= new Dictionary<SphereGridKind, Dictionary<int, SphereGridCharacter>>();
        Dictionary<int, SphereGridCharacter> storedColors = colors
            .Where(pair => pair.Key >= 0 && pair.Value != SphereGridCharacter.Unassigned)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (storedColors.Count == 0)
            document.Grids.Remove(kind);
        else
            document.Grids[kind] = storedColors;

        string path = GetPath(directory);
        string temporaryPath = path + ".zwtmp";
        if (document.Grids.Count == 0)
        {
            DeleteIfExists(path);
            DeleteIfExists(temporaryPath);
            return;
        }

        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public static void Delete(string directory)
    {
        DeleteIfExists(GetPath(directory));
        DeleteIfExists(GetPath(directory) + ".zwtmp");
    }

    public static void MigrateLegacyLocation(
        string metadataDirectory, string sphereGridDirectory)
    {
        string legacyPath = Path.Combine(sphereGridDirectory, FileName);
        string legacyTemporaryPath = legacyPath + ".zwtmp";
        if (!File.Exists(legacyPath))
        {
            DeleteIfExists(legacyTemporaryPath);
            return;
        }

        string metadataPath = GetPath(metadataDirectory);
        if (!File.Exists(metadataPath))
        {
            Directory.CreateDirectory(metadataDirectory);
            File.Move(legacyPath, metadataPath);
        }
        else
        {
            // The dedicated metadata copy is authoritative after migration.
            DeleteIfExists(legacyPath);
        }
        DeleteIfExists(legacyTemporaryPath);
    }

    public static string GetPath(string directory) => Path.Combine(directory, FileName);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static MetadataDocument Read(string directory)
    {
        string path = GetPath(directory);
        if (!File.Exists(path))
            return new MetadataDocument();
        return JsonSerializer.Deserialize<MetadataDocument>(File.ReadAllText(path), JsonOptions)
            ?? new MetadataDocument();
    }

    private sealed class MetadataDocument
    {
        public int Version { get; set; } = 1;

        public Dictionary<SphereGridKind, Dictionary<int, SphereGridCharacter>>? Grids
        {
            get;
            set;
        } = new();
    }
}
