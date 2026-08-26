using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.IO;

namespace FFXProjectEditor.Services;

public sealed class ProjectManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string MasterPath { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime LastOpenedUtc { get; set; }
    public Guid? CreatedFromProjectId { get; set; }
}

public sealed class ProjectRegistryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid? ActiveProjectId { get; set; }
    public List<ProjectRegistryEntry> Projects { get; set; } = new();
    public List<string> RecentUnregisteredMasterPaths { get; set; } = new();
}

public sealed class ProjectRegistryEntry
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
}

public static class ProjectRegistry_Service
{
    private const int MaxProjectNameLength = 80;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ProjectsRoot => Path.Combine(ProgramMetadata_Service.RootPath, "projects");
    public static string RegistryPath => Path.Combine(ProgramMetadata_Service.RootPath, "projects.json");
    public static ProjectRegistryDocument Registry { get; private set; } = Load();

    public static IReadOnlyList<ProjectManifest> GetProjects() => Registry.Projects
        .Select(entry => TryLoadManifest(entry.Name))
        .Where(manifest => manifest is not null)
        .Cast<ProjectManifest>()
        .OrderByDescending(manifest => manifest.LastOpenedUtc)
        .ToList();

    public static ProjectManifest? FindByPath(string masterPath)
    {
        string normalized = NormalizePath(masterPath);
        return GetProjects().FirstOrDefault(project =>
            string.Equals(NormalizePath(project.MasterPath), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static ProjectManifest Register(string name, string masterPath, Guid? createdFromProjectId = null)
    {
        lock (Sync)
        {
            string validatedName = ValidateName(name);
            if (Registry.Projects.Any(project =>
                string.Equals(project.Name, validatedName, StringComparison.OrdinalIgnoreCase)) ||
                Directory.Exists(Path.Combine(ProjectsRoot, validatedName)))
                throw new InvalidOperationException($"A project named '{validatedName}' already exists.");

            DateTime now = DateTime.UtcNow;
            var manifest = new ProjectManifest
            {
                ProjectId = Guid.NewGuid(),
                Name = validatedName,
                MasterPath = NormalizePath(masterPath),
                CreatedUtc = now,
                LastOpenedUtc = now,
                CreatedFromProjectId = createdFromProjectId
            };
            string directory = GetProjectDirectory(validatedName);
            Guid? previousActiveProjectId = Registry.ActiveProjectId;
            List<string> previousRecentPaths = Registry.RecentUnregisteredMasterPaths.ToList();
            var entry = new ProjectRegistryEntry { ProjectId = manifest.ProjectId, Name = manifest.Name };
            try
            {
                Directory.CreateDirectory(directory);
                WriteJsonAtomic(Path.Combine(directory, "manifest.json"), manifest);
                Registry.Projects.Add(entry);
                Registry.RecentUnregisteredMasterPaths.RemoveAll(path =>
                    string.Equals(NormalizePath(path), manifest.MasterPath, StringComparison.OrdinalIgnoreCase));
                Registry.ActiveProjectId = manifest.ProjectId;
                SaveRegistry();
                return manifest;
            }
            catch
            {
                Registry.Projects.Remove(entry);
                Registry.ActiveProjectId = previousActiveProjectId;
                Registry.RecentUnregisteredMasterPaths = previousRecentPaths;
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch { }
                throw;
            }
        }
    }

    public static void RollbackNewProject(Guid projectId, Guid? restoreActiveProjectId)
    {
        lock (Sync)
        {
            ProjectRegistryEntry entry = Registry.Projects.FirstOrDefault(project => project.ProjectId == projectId)
                ?? throw new InvalidOperationException("The new project registration could not be found for rollback.");
            string directory = GetProjectDirectory(entry.Name);
            Registry.Projects.Remove(entry);
            Registry.ActiveProjectId = restoreActiveProjectId;
            try { SaveRegistry(); }
            catch
            {
                Registry.Projects.Add(entry);
                Registry.ActiveProjectId = projectId;
                throw;
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    public static void RememberUnregisteredPath(string masterPath)
    {
        lock (Sync)
        {
            string normalized = NormalizePath(masterPath);
            if (FindByPath(normalized) is not null) return;
            Registry.RecentUnregisteredMasterPaths.RemoveAll(path =>
                string.Equals(NormalizePath(path), normalized, StringComparison.OrdinalIgnoreCase));
            Registry.RecentUnregisteredMasterPaths.Insert(0, normalized);
            if (Registry.RecentUnregisteredMasterPaths.Count > 5)
                Registry.RecentUnregisteredMasterPaths.RemoveRange(
                    5, Registry.RecentUnregisteredMasterPaths.Count - 5);
            SaveRegistry();
        }
    }

    public static void MarkOpened(ProjectManifest manifest)
    {
        lock (Sync)
        {
            DateTime openedUtc = DateTime.UtcNow;
            var preparedManifest = new ProjectManifest
            {
                SchemaVersion = manifest.SchemaVersion,
                ProjectId = manifest.ProjectId,
                Name = manifest.Name,
                MasterPath = manifest.MasterPath,
                CreatedUtc = manifest.CreatedUtc,
                LastOpenedUtc = openedUtc,
                CreatedFromProjectId = manifest.CreatedFromProjectId
            };
            var preparedRegistry = new ProjectRegistryDocument
            {
                SchemaVersion = Registry.SchemaVersion,
                ActiveProjectId = manifest.ProjectId,
                Projects = Registry.Projects.Select(entry => new ProjectRegistryEntry
                {
                    ProjectId = entry.ProjectId,
                    Name = entry.Name
                }).ToList(),
                RecentUnregisteredMasterPaths = Registry.RecentUnregisteredMasterPaths.ToList()
            };

            MultiFileSaveTransaction.Save([
                new FileReplacement(
                    Path.Combine(GetProjectDirectory(manifest.Name), "manifest.json"),
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preparedManifest, JsonOptions))),
                new FileReplacement(
                    RegistryPath,
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preparedRegistry, JsonOptions)))
            ]);

            manifest.LastOpenedUtc = openedUtc;
            Registry.ActiveProjectId = manifest.ProjectId;
        }
    }

    public static ProjectManifest Relink(Guid projectId, string masterPath)
    {
        lock (Sync)
        {
            if (Project_Service.Instance.ActiveProject?.ProjectId == projectId)
                throw new InvalidOperationException(
                    "The active project cannot be relinked. Open another project before changing this project's folder.");
            ProjectRegistryEntry entry = Registry.Projects.FirstOrDefault(project => project.ProjectId == projectId)
                ?? throw new InvalidOperationException("The project is no longer registered.");
            ProjectManifest manifest = TryLoadManifest(entry.Name)
                ?? throw new InvalidOperationException("The project manifest is missing or unreadable.");
            string normalized = NormalizePath(masterPath);
            if (!Directory.Exists(normalized) ||
                !string.Equals(Path.GetFileName(normalized), "master", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select an existing FFX Master folder.");
            ProjectManifest? duplicate = GetProjects().FirstOrDefault(project => project.ProjectId != projectId &&
                string.Equals(NormalizePath(project.MasterPath), normalized, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
                throw new InvalidOperationException($"That Master folder is already registered as '{duplicate.Name}'.");
            manifest.MasterPath = normalized;
            manifest.LastOpenedUtc = DateTime.UtcNow;
            WriteJsonAtomic(Path.Combine(GetProjectDirectory(manifest.Name), "manifest.json"), manifest);
            return manifest;
        }
    }

    public static void ForgetProject(Guid projectId)
    {
        lock (Sync)
        {
            if (Project_Service.Instance.ActiveProject?.ProjectId == projectId)
                throw new InvalidOperationException(
                    "The active project cannot be forgotten. Open another project before forgetting this project.");
            ProjectRegistryEntry? entry = Registry.Projects.FirstOrDefault(project => project.ProjectId == projectId);
            if (entry is null) return;
            string source = GetProjectDirectory(entry.Name);
            string? destination = null;
            if (Directory.Exists(source))
            {
                string archiveRoot = Path.Combine(ProgramMetadata_Service.RootPath, "removed-projects");
                Directory.CreateDirectory(archiveRoot);
                destination = Path.Combine(archiveRoot,
                    $"{entry.Name}-{DateTime.UtcNow:yyyyMMddHHmmss}-{projectId:N}");
                Directory.Move(source, destination);
            }
            try
            {
                Registry.Projects.Remove(entry);
                if (Registry.ActiveProjectId == projectId) Registry.ActiveProjectId = null;
                SaveRegistry();
            }
            catch
            {
                Registry.Projects.Add(entry);
                if (destination is not null && Directory.Exists(destination) && !Directory.Exists(source))
                    Directory.Move(destination, source);
                throw;
            }
        }
    }

    public static string ValidateName(string name)
    {
        string result = (name ?? "").Trim();
        if (result.Length == 0) throw new InvalidOperationException("Enter a project name.");
        if (result.Length > MaxProjectNameLength)
            throw new InvalidOperationException($"Project names cannot exceed {MaxProjectNameLength} characters.");
        if (result is "." or ".." || result.EndsWith('.') || result.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("The project name contains characters that cannot be used in a folder name.");
        string stem = result.Split('.')[0];
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("That project name is reserved by Windows.");
        return result;
    }

    public static string ValidateNewName(string name)
    {
        string result = ValidateName(name);
        if (Registry.Projects.Any(project =>
                string.Equals(project.Name, result, StringComparison.OrdinalIgnoreCase)) ||
            Directory.Exists(Path.Combine(ProjectsRoot, result)))
            throw new InvalidOperationException($"A project named '{result}' already exists.");
        return result;
    }

    private static ProjectRegistryDocument Load()
    {
        ProjectRegistryDocument registry;
        try
        {
            if (File.Exists(RegistryPath))
            {
                registry =
                    JsonSerializer.Deserialize<ProjectRegistryDocument>(File.ReadAllText(RegistryPath), JsonOptions)
                    ?? new ProjectRegistryDocument();
                registry.Projects ??= new List<ProjectRegistryEntry>();
                registry.RecentUnregisteredMasterPaths ??= new List<string>();
                MigrateRecentProjects(registry);
                RecoverManifestEntries(registry);
                return registry;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read projects.json: {ex.Message}");
            try
            {
                string quarantine = RegistryPath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
                if (File.Exists(RegistryPath)) File.Move(RegistryPath, quarantine, false);
            }
            catch { }
        }
        var created = new ProjectRegistryDocument();
        MigrateRecentProjects(created);
        RecoverManifestEntries(created);
        return created;
    }

    private static void RecoverManifestEntries(ProjectRegistryDocument registry)
    {
        if (!Directory.Exists(ProjectsRoot)) return;
        bool changed = false;
        foreach (string directory in Directory.EnumerateDirectories(ProjectsRoot))
        {
            try
            {
                string manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                ProjectManifest? manifest = JsonSerializer.Deserialize<ProjectManifest>(
                    File.ReadAllText(manifestPath), JsonOptions);
                if (manifest is null || manifest.ProjectId == Guid.Empty) continue;
                string validatedName = ValidateName(manifest.Name);
                if (!string.Equals(Path.GetFileName(directory), validatedName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (registry.Projects.Any(entry => entry.ProjectId == manifest.ProjectId ||
                    string.Equals(entry.Name, validatedName, StringComparison.OrdinalIgnoreCase))) continue;
                registry.Projects.Add(new ProjectRegistryEntry
                    { ProjectId = manifest.ProjectId, Name = validatedName });
                changed = true;
            }
            catch (Exception ex) { Debug.WriteLine($"Could not recover project manifest: {ex.Message}"); }
        }
        if (changed) WriteJsonAtomic(RegistryPath, registry);
    }

    private static ProjectManifest? TryLoadManifest(string projectName)
    {
        try
        {
            string path = Path.Combine(GetProjectDirectory(ValidateName(projectName)), "manifest.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read project '{projectName}': {ex.Message}");
            return null;
        }
    }

    private static string GetProjectDirectory(string name) => Path.Combine(ProjectsRoot, name);
    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static void SaveRegistry() => WriteJsonAtomic(RegistryPath, Registry);

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private static void MigrateRecentProjects(ProjectRegistryDocument registry)
    {
        try
        {
            string legacyPath = ProgramMetadata_Service.GetFilePath("recent-projects.txt");
            if (!File.Exists(legacyPath)) return;
            foreach (string path in File.ReadAllLines(legacyPath).Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string normalized = NormalizePath(path);
                if (!registry.RecentUnregisteredMasterPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    registry.RecentUnregisteredMasterPaths.Add(normalized);
            }
            registry.RecentUnregisteredMasterPaths = registry.RecentUnregisteredMasterPaths.Take(5).ToList();
            Directory.CreateDirectory(ProgramMetadata_Service.RootPath);
            string temporaryPath = RegistryPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registry, JsonOptions));
            File.Move(temporaryPath, RegistryPath, true);
            File.Delete(legacyPath);
        }
        catch (Exception ex) { Debug.WriteLine($"Could not migrate recent projects: {ex.Message}"); }
    }
}
