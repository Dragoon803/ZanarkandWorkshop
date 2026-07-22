using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FFXProjectEditor.Services;

public static class VanillaReference_Service
{
	private sealed class TrustedManifest
	{
		public int ManifestVersion { get; set; }
		public string ReferenceId { get; set; } = "";
		public int FileCount { get; set; }
		public List<TrustedManifestFile> Files { get; set; } = new();
	}

	private sealed class TrustedManifestFile
	{
		public string Path { get; set; } = "";
		public long Size { get; set; }
		public string Sha256 { get; set; } = "";
	}

	private static readonly string ManifestPath = Path.Combine(
		AppContext.BaseDirectory, "Assets", "trusted-vanilla-manifest.json");

	public sealed record ValidationResult(
		bool IsValid,
		string Classification,
		string Summary,
		IReadOnlyList<string> Problems,
		int MonsterFilesChecked,
		int KernelFilesChecked);

	private static readonly string[] RequiredKernelFiles =
	{
		"command.bin", "monmagic1.bin", "monmagic2.bin", "item.bin"
	};

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "vanilla-master.txt");

    public static string? MasterPath { get; private set; } = LoadSavedPath();

    public static bool IsConfigured => TryValidate(MasterPath, out _);

    public static string NormalizeMasterPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string fullPath = Path.GetFullPath(path.Trim());
        fullPath = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    public static bool IsProtectedVanillaPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(MasterPath)) return false;
        try
        {
            return string.Equals(NormalizeMasterPath(path), NormalizeMasterPath(MasterPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryValidate(string? path, out string message)
    {
		ValidationResult result = Validate(path);
		message = result.IsValid ? result.Summary : string.Join(Environment.NewLine, result.Problems);
		return result.IsValid;
    }

	public static ValidationResult Validate(string? path)
	{
		var problems = new List<string>();
		if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
			return Invalid("No Original Game Files folder is configured.", problems);

		string normalized;
		try { normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
		catch (Exception ex) { return Invalid("The selected path is invalid: " + ex.Message, problems); }

		if (!string.Equals(Path.GetFileName(normalized), "master", StringComparison.OrdinalIgnoreCase))
			problems.Add("The selected folder must be named master.");

		string monPath = Path.Combine(normalized, "jppc", "battle", "mon");
		string kernelPath = Path.Combine(normalized, "new_uspc", "battle", "kernel");
		if (!Directory.Exists(monPath)) problems.Add("Missing folder: jppc\\battle\\mon");
		if (!Directory.Exists(kernelPath)) problems.Add("Missing folder: new_uspc\\battle\\kernel");
		if (problems.Count > 0) return Invalid(null, problems);

		int monsterFilesChecked = 0;
		for (int i = 0; i <= 360; i++)
		{
			string relative = Path.Combine($"_m{i:000}", $"m{i:000}.bin");
			string file = Path.Combine(monPath, relative);
			if (!File.Exists(file))
			{
				problems.Add("Missing monster file: " + Path.Combine("jppc", "battle", "mon", relative));
				continue;
			}
			if (CanReadNonEmpty(file, out string? problem)) monsterFilesChecked++;
			else problems.Add(problem!);
		}

		int kernelFilesChecked = 0;
		foreach (string name in RequiredKernelFiles)
		{
			string file = Path.Combine(kernelPath, name);
			if (!File.Exists(file))
			{
				problems.Add("Missing kernel file: " + Path.Combine("new_uspc", "battle", "kernel", name));
				continue;
			}
			if (CanReadNonEmpty(file, out string? problem)) kernelFilesChecked++;
			else problems.Add(problem!);
		}

		if (problems.Count > 0)
			return new ValidationResult(false, "Invalid", BuildFailureSummary(problems), problems,
				monsterFilesChecked, kernelFilesChecked);

		if (!TryLoadTrustedManifest(out TrustedManifest? manifest, out string manifestProblem))
		{
			string unverifiedSummary = $"Structurally Valid but Unverified: checked {monsterFilesChecked} monster files and " +
				$"{kernelFilesChecked} recovery-critical kernel files. {manifestProblem}";
			return new ValidationResult(true, "Structurally Valid but Unverified", unverifiedSummary,
				Array.Empty<string>(), monsterFilesChecked, kernelFilesChecked);
		}

		var differences = new List<string>();
		foreach (TrustedManifestFile expected in manifest!.Files)
		{
			string relative = expected.Path.Replace('/', Path.DirectorySeparatorChar);
			string candidate = Path.Combine(normalized, relative);
			if (!File.Exists(candidate))
			{
				differences.Add("Missing trusted file: " + expected.Path);
				continue;
			}

			var info = new FileInfo(candidate);
			if (info.Length != expected.Size)
			{
				differences.Add($"Size differs: {expected.Path} (expected {expected.Size}, found {info.Length})");
				continue;
			}

			try
			{
				string actualHash = ComputeSha256(candidate);
				if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
					differences.Add("Contents differ: " + expected.Path);
			}
			catch (Exception ex)
			{
				differences.Add($"Could not hash {expected.Path}: {ex.Message}");
			}
		}

		if (differences.Count > 0)
		{
			string modifiedSummary = "Modified or Unrecognized: the folder is structurally complete, but it does not " +
				$"match trusted reference '{manifest.ReferenceId}'. It may contain modified files or belong to a different game version." +
				Environment.NewLine + BuildFailureSummary(differences);
			return new ValidationResult(false, "Modified or Unrecognized", modifiedSummary, differences,
				monsterFilesChecked, kernelFilesChecked);
		}

		string verifiedSummary = $"Verified Original Files: all {manifest.Files.Count} recovery files match trusted reference " +
			$"'{manifest.ReferenceId}' by exact size and SHA-256 hash.";
		return new ValidationResult(true, "Verified Original Files", verifiedSummary, Array.Empty<string>(),
			monsterFilesChecked, kernelFilesChecked);
	}

    public static void Configure(string path)
    {
		ValidationResult validation = Validate(path);
		if (!validation.IsValid)
			throw new InvalidOperationException(validation.Summary);

		string normalized = NormalizeMasterPath(path);
		if (!string.IsNullOrWhiteSpace(Project_Service.Instance.ProjectPath) &&
			string.Equals(normalized, NormalizeMasterPath(Project_Service.Instance.ProjectPath),
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The Original Game Files folder must be separate from the active editing project.");
		MasterPath = normalized;
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, MasterPath);
    }

	private static bool CanReadNonEmpty(string path, out string? problem)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (stream.Length <= 0)
			{
				problem = "File is empty: " + path;
				return false;
			}
			_ = stream.ReadByte();
			problem = null;
			return true;
		}
		catch (Exception ex)
		{
			problem = $"Cannot read {path}: {ex.Message}";
			return false;
		}
	}

	private static bool TryLoadTrustedManifest(out TrustedManifest? manifest, out string problem)
	{
		manifest = null;
		try
		{
			if (!File.Exists(ManifestPath))
			{
				problem = "The packaged trusted hash manifest could not be found.";
				return false;
			}
			manifest = JsonSerializer.Deserialize<TrustedManifest>(File.ReadAllText(ManifestPath),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (manifest is null || manifest.ManifestVersion != 1 || manifest.Files.Count == 0 ||
				manifest.FileCount != manifest.Files.Count)
			{
				problem = "The packaged trusted hash manifest is invalid.";
				manifest = null;
				return false;
			}
			problem = "";
			return true;
		}
		catch (Exception ex)
		{
			problem = "The packaged trusted hash manifest could not be read: " + ex.Message;
			manifest = null;
			return false;
		}
	}

	private static string ComputeSha256(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
	}

	private static ValidationResult Invalid(string? problem, List<string> problems)
	{
		if (!string.IsNullOrWhiteSpace(problem)) problems.Add(problem);
		return new ValidationResult(false, "Invalid", BuildFailureSummary(problems), problems, 0, 0);
	}

	private static string BuildFailureSummary(IReadOnlyList<string> problems)
	{
		if (problems.Count == 0) return "The selected folder is not a valid recovery source.";
		const int displayLimit = 8;
		string details = string.Join(Environment.NewLine, problems.Take(displayLimit).Select(p => "• " + p));
		if (problems.Count > displayLimit)
			details += $"{Environment.NewLine}• …and {problems.Count - displayLimit} more problem(s).";
		return $"Validation found {problems.Count} problem(s):{Environment.NewLine}{details}";
	}

    public static string? ResolveProjectFile(string editedFilePath)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(Project_Service.Instance.ProjectPath)) return null;
        string activeMaster = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Project_Service.Instance.ProjectPath));
		if (string.Equals(activeMaster, MasterPath, StringComparison.OrdinalIgnoreCase)) return null;
        string editedFullPath = Path.GetFullPath(editedFilePath);
        string relativePath = Path.GetRelativePath(activeMaster, editedFullPath);
        if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
            return null;

        string candidate = Path.GetFullPath(Path.Combine(MasterPath!, relativePath));
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? LoadSavedPath()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            string path = File.ReadAllText(SettingsPath).Trim();
            return NormalizeMasterPath(path);
        }
        catch
        {
            return null;
        }
    }
}
