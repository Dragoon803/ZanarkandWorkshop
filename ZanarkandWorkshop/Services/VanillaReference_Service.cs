using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.IO;

namespace FFXProjectEditor.Services;

public enum RecoverySourceState { Invalid, VerificationUnavailable, UnrecognizedSource, VerifiedKnownReference }
public enum RecoveryFileTrust { Verified, Unrecognized, NotInManifest, Missing, Unreadable }
public enum RecoveryDifferenceType { Missing, SizeMismatch, HashMismatch, Unreadable, ExtraFile }

public sealed record RecoveryFileDifference(string RelativePath, RecoveryDifferenceType DifferenceType,
    long? ExpectedSize, long? ActualSize, string? ExpectedSha256, string? ActualSha256);

public sealed record RecoveryFileVerification(string RelativePath, string? SourcePath, RecoveryFileTrust Trust,
    long? ExpectedSize, long? ActualSize, string? ExpectedSha256, string? ActualSha256)
{
    public bool RequiresWarning => Trust is RecoveryFileTrust.Unrecognized or RecoveryFileTrust.NotInManifest;
    public bool CanRestore => Trust is RecoveryFileTrust.Verified or RecoveryFileTrust.Unrecognized or RecoveryFileTrust.NotInManifest;
}

public static class VanillaReference_Service
{
    private sealed class TrustedManifest
    {
        public int ManifestVersion { get; set; }
        public string ReferenceId { get; set; } = "";
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public List<TrustedManifestFile> Files { get; set; } = new();
    }

    private sealed class TrustedManifestFile
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
    }

    public sealed record ValidationResult(RecoverySourceState State, string Classification, string Summary,
        IReadOnlyList<string> Problems, IReadOnlyList<RecoveryFileDifference> Differences,
        int MonsterFilesChecked, int KernelFilesChecked, string? ReferenceId, string Fingerprint)
    {
        public bool IsStructurallyValid => State != RecoverySourceState.Invalid;
        public bool IsKnownReference => State == RecoverySourceState.VerifiedKnownReference;
        public bool CanConfigure => IsStructurallyValid;
        public bool RequiresAcceptance => State is RecoverySourceState.VerificationUnavailable or RecoverySourceState.UnrecognizedSource;
        public bool IsValid => CanConfigure;
    }

    public sealed record FolderRestoreResult(string RelativeFolder, int FilesRestored);
    public sealed record FolderRestorePreview(string SourceFolder, string RelativeFolder, int FileCount,
        int VerifiedCount, int UnrecognizedCount, int NotInManifestCount,
        IReadOnlyList<RecoveryFileVerification> Files);

    private static string ManifestPath =>
        Environment.GetEnvironmentVariable("ZANARKAND_WORKSHOP_RECOVERY_MANIFEST") is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(AppContext.BaseDirectory, "Assets", "trusted-vanilla-manifest.json");
    private static readonly object Sync = new();
    private static TrustedManifest? _manifest;
    private static Dictionary<string, TrustedManifestFile>? _manifestByPath;
    private static string? _manifestProblem;
    private static bool _manifestLoadAttempted;
    private static string? _cachedPath;
    private static ValidationResult? _cachedValidation;
    private static string? _acceptedFingerprint;

    public static string? MasterPath { get; private set; } = LoadSavedPath();

    public static ValidationResult? GetCachedValidation()
    {
        lock (Sync)
        {
            if (_cachedValidation is null || string.IsNullOrWhiteSpace(MasterPath) ||
                !string.Equals(_cachedPath, MasterPath, StringComparison.OrdinalIgnoreCase)) return null;
            return _cachedValidation;
        }
    }

    public static bool IsConfigured
    {
        get
        {
            ValidationResult result = GetCachedValidation() ?? Validate(MasterPath);
            return result.CanConfigure && (!result.RequiresAcceptance || IsAccepted(result));
        }
    }

    public static string NormalizeMasterPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string fullPath = Path.GetFullPath(path.Trim()).Replace(
            Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    public static bool IsProtectedVanillaPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(MasterPath)) return false;
        try { return string.Equals(NormalizeMasterPath(path), NormalizeMasterPath(MasterPath), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static bool TryValidate(string? path, out string message)
    {
        ValidationResult result = Validate(path);
        bool usable = result.CanConfigure && (!result.RequiresAcceptance || IsAccepted(result));
        message = usable ? result.Summary : result.Summary + (result.RequiresAcceptance
            ? Environment.NewLine + "This source requires explicit acceptance for this session." : "");
        return usable;
    }

    public static ValidationResult Validate(string? path) => Validate(path, false);

    public static ValidationResult Validate(string? path, bool forceRefresh)
    {
        string? normalizedCandidate = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { normalizedCandidate = NormalizeMasterPath(path); }
            catch { normalizedCandidate = path; }
        }
        lock (Sync)
        {
            if (!forceRefresh && _cachedValidation is not null &&
                string.Equals(_cachedPath, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return _cachedValidation;
        }

        ValidationResult result = ValidateCore(path);
        if (normalizedCandidate is not null)
        {
            lock (Sync) { _cachedPath = normalizedCandidate; _cachedValidation = result; }
        }
        return result;
    }

    private static ValidationResult ValidateCore(string? path)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return Invalid("No Original Game Files folder is configured.", problems);
        string normalized;
        try { normalized = NormalizeMasterPath(path); }
        catch (Exception ex) { return Invalid("The selected path is invalid: " + ex.Message, problems); }
        if (!string.Equals(Path.GetFileName(normalized), "master", StringComparison.OrdinalIgnoreCase))
            problems.Add("The selected folder must be named master.");
        if (!Directory.Exists(Path.Combine(normalized, "jppc"))) problems.Add("Missing base folder: jppc");
        if (!Directory.Exists(Path.Combine(normalized, "new_uspc"))) problems.Add("Missing base folder: new_uspc");
        if (problems.Count > 0) return Invalid(null, problems);

        const string summary = "Original Game Files are configured. Recovery files will be verified against the " +
            "complete reference manifest only when an editor needs them or when Verify Recovery Files is selected.";
        return new ValidationResult(RecoverySourceState.VerifiedKnownReference,
            "Configured — files verified as needed", summary, [], [], 0, 0, null,
            Fingerprint("configured", [normalized]));
    }

    public static ValidationResult VerifyRecoveryFiles()
    {
        ValidationResult structure = Validate(MasterPath);
        if (!structure.CanConfigure || string.IsNullOrWhiteSpace(MasterPath)) return structure;
        List<string> paths = GetCurrentRecoveryPaths(MasterPath);
        List<RecoveryFileVerification> files = paths.Select(VerifySourceRelativeFile).ToList();
        int verified = files.Count(file => file.Trust == RecoveryFileTrust.Verified);
        int unrecognized = files.Count(file => file.Trust == RecoveryFileTrust.Unrecognized);
        int notInManifest = files.Count(file => file.Trust == RecoveryFileTrust.NotInManifest);
        int missing = files.Count(file => file.Trust == RecoveryFileTrust.Missing);
        int unreadable = files.Count(file => file.Trust == RecoveryFileTrust.Unreadable);
        List<RecoveryFileDifference> differences = files.Where(file => file.Trust != RecoveryFileTrust.Verified)
            .Select(ToDifference).ToList();
        TryLoadTrustedManifest(out TrustedManifest? manifest, out string manifestProblem);
        string summary = $"Checked {files.Count:N0} files currently used by Recovery: {verified:N0} verified, " +
            $"{unrecognized:N0} unrecognized, {notInManifest:N0} not in reference, {missing:N0} missing, " +
            $"and {unreadable:N0} unreadable.";
        if (manifest is null) summary += Environment.NewLine + manifestProblem;
        if (differences.Count > 0) summary += Environment.NewLine + BuildDifferenceSummary(differences);
        RecoverySourceState state = differences.Count == 0
            ? RecoverySourceState.VerifiedKnownReference
            : RecoverySourceState.UnrecognizedSource;
        string reference = manifest?.ReferenceId ?? "unavailable";
        return new ValidationResult(state,
            differences.Count == 0 ? "Recovery Files Verified" : "Recovery Files Need Attention",
            summary, differences.Select(DescribeDifference).ToList(), differences, 361, 8,
            manifest?.ReferenceId, BuildVerificationFingerprint(reference, differences));
    }

    public static void Configure(string path, bool acceptUnverified = false)
    {
        ValidationResult validation = Validate(path);
        if (!validation.CanConfigure) throw new InvalidOperationException(validation.Summary);
        if (validation.RequiresAcceptance && !acceptUnverified)
            throw new InvalidOperationException(validation.Summary + Environment.NewLine +
                "Explicit acceptance is required for this session.");
        string normalized = NormalizeMasterPath(path);
        if (!string.IsNullOrWhiteSpace(Project_Service.Instance.ProjectPath) &&
            string.Equals(normalized, NormalizeMasterPath(Project_Service.Instance.ProjectPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Original Game Files folder must be separate from the active editing project.");
        MasterPath = normalized;
        _acceptedFingerprint = validation.RequiresAcceptance ? validation.Fingerprint : null;
        AppSettings_Service.Current.OriginalGameFiles.MasterPath = MasterPath;
        AppSettings_Service.Save();
    }

    public static bool IsAccepted(ValidationResult result) => !result.RequiresAcceptance ||
        string.Equals(_acceptedFingerprint, result.Fingerprint, StringComparison.Ordinal);

    public static string BuildDiagnostics(ValidationResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("Zanarkand Workshop Recovery verification diagnostics");
        text.AppendLine($"State: {result.Classification}");
        text.AppendLine($"Reference: {result.ReferenceId ?? "Unavailable"}");
        text.AppendLine($"Verification time (UTC): {DateTime.UtcNow:O}");
        text.AppendLine($"Differences: {result.Differences.Count}");
        foreach (RecoveryFileDifference difference in result.Differences)
        {
            text.AppendLine().AppendLine($"Path: {difference.RelativePath}");
            text.AppendLine($"Difference: {difference.DifferenceType}");
            text.AppendLine($"Expected size: {difference.ExpectedSize?.ToString() ?? "n/a"}");
            text.AppendLine($"Actual size: {difference.ActualSize?.ToString() ?? "n/a"}");
            text.AppendLine($"Expected SHA-256: {difference.ExpectedSha256 ?? "n/a"}");
            text.AppendLine($"Actual SHA-256: {difference.ActualSha256 ?? "n/a"}");
        }
        return text.ToString().TrimEnd();
    }

    public static RecoveryFileVerification VerifyProjectFile(string editedFilePath)
    {
        if (string.IsNullOrWhiteSpace(MasterPath) || string.IsNullOrWhiteSpace(Project_Service.Instance.ProjectPath))
            return new("", null, RecoveryFileTrust.Missing, null, null, null, null);
        string activeMaster = NormalizeMasterPath(Project_Service.Instance.ProjectPath);
        string relative = Path.GetRelativePath(activeMaster, Path.GetFullPath(editedFilePath));
        if (relative == "." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return new(NormalizeRelative(relative), null, RecoveryFileTrust.Missing, null, null, null, null);
        return VerifySourceRelativeFile(NormalizeRelative(relative));
    }

    private static RecoveryFileVerification VerifySourceRelativeFile(string relative)
    {
        string normalizedRelative = NormalizeRelative(relative);
        if (string.IsNullOrWhiteSpace(MasterPath))
            return new(normalizedRelative, null, RecoveryFileTrust.Missing, null, null, null, null);
        string source = Path.GetFullPath(Path.Combine(MasterPath, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(source)) return new(normalizedRelative, source, RecoveryFileTrust.Missing, null, null, null, null);
        long actualSize = new FileInfo(source).Length;
        string actualHash;
        try { actualHash = ComputeSha256(source); }
        catch { return new(normalizedRelative, source, RecoveryFileTrust.Unreadable, null, actualSize, null, null); }
        if (!TryGetManifestFile(normalizedRelative, out TrustedManifestFile? expected))
            return new(normalizedRelative, source, RecoveryFileTrust.NotInManifest, null, actualSize, null, actualHash);
        bool match = actualSize == expected!.Size && string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase);
        return new(normalizedRelative, source, match ? RecoveryFileTrust.Verified : RecoveryFileTrust.Unrecognized,
            expected.Size, actualSize, expected.Sha256, actualHash);
    }

    private static List<string> GetCurrentRecoveryPaths(string masterPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index <= 360; index++)
            paths.Add($"jppc/battle/mon/_m{index:000}/m{index:000}.bin");
        foreach (string relative in new[]
        {
            "new_uspc/battle/kernel/command.bin", "new_uspc/battle/kernel/item.bin",
            "new_uspc/battle/kernel/monmagic1.bin", "new_uspc/battle/kernel/monmagic2.bin",
            "new_uspc/battle/kernel/a_ability.bin", "jppc/battle/kernel/kaizou.bin",
            "jppc/battle/kernel/prepare.bin", "jppc/battle/kernel/takara.bin",
            "jppc/battle/kernel/btl.bin",
            "jppc/menu/abmap/dat01.dat", "jppc/menu/abmap/dat02.dat", "jppc/menu/abmap/dat03.dat",
            "jppc/menu/abmap/dat09.dat", "jppc/menu/abmap/dat10.dat", "jppc/menu/abmap/dat11.dat"
        }) paths.Add(relative);

        string battleRoot = Path.Combine(masterPath, "jppc", "battle", "btl");
        if (Directory.Exists(battleRoot))
            foreach (string file in EnumerateRegularFiles(battleRoot))
                paths.Add(NormalizeRelative(Path.GetRelativePath(masterPath, file)));
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static RecoveryFileDifference ToDifference(RecoveryFileVerification file) => file.Trust switch
    {
        RecoveryFileTrust.Missing => new(file.RelativePath, RecoveryDifferenceType.Missing,
            file.ExpectedSize, null, file.ExpectedSha256, null),
        RecoveryFileTrust.Unreadable => new(file.RelativePath, RecoveryDifferenceType.Unreadable,
            file.ExpectedSize, file.ActualSize, file.ExpectedSha256, null),
        RecoveryFileTrust.NotInManifest => new(file.RelativePath, RecoveryDifferenceType.ExtraFile,
            null, file.ActualSize, null, file.ActualSha256),
        _ when file.ExpectedSize != file.ActualSize => new(file.RelativePath, RecoveryDifferenceType.SizeMismatch,
            file.ExpectedSize, file.ActualSize, file.ExpectedSha256, file.ActualSha256),
        _ => new(file.RelativePath, RecoveryDifferenceType.HashMismatch,
            file.ExpectedSize, file.ActualSize, file.ExpectedSha256, file.ActualSha256)
    };

    public static string BuildRestoreTrustNotice(IEnumerable<RecoveryFileVerification> files)
    {
        List<RecoveryFileVerification> uncertain = files.Where(file => file.RequiresWarning).ToList();
        if (uncertain.Count == 0) return "";
        var text = new StringBuilder(Environment.NewLine + Environment.NewLine + "Verification warning:" + Environment.NewLine);
        foreach (RecoveryFileVerification file in uncertain.Take(12))
            text.AppendLine($"• {file.RelativePath} — {(file.Trust == RecoveryFileTrust.Unrecognized ? "does not match the known reference" : "not included in the trusted reference manifest")}");
        if (uncertain.Count > 12) text.AppendLine($"• …and {uncertain.Count - 12} more file(s)");
        text.Append("Zanarkand Workshop cannot confirm that the file(s) above match a known original reference. " +
            "This does not prove they are modified or corrupted. Continue only if you are confident this source came from a clean extraction.");
        return text.ToString();
    }

    public static string? ResolveProjectFile(string editedFilePath)
    {
        if (!IsConfigured) return null;
        RecoveryFileVerification verification = VerifyProjectFile(editedFilePath);
        return verification.CanRestore ? verification.SourcePath : null;
    }

    /// <summary>
    /// Re-verifies one recovery input immediately before it is used. Files that
    /// do not match the trusted manifest require an explicit user-approved path.
    /// </summary>
    public static string ResolveAuthorizedProjectFile(string editedFilePath, bool allowUnverified)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Configure and accept Original Game Files before restoring a file.");
        RecoveryFileVerification verification = VerifyProjectFile(editedFilePath);
        EnsureRestoreAllowed(verification, allowUnverified);
        return verification.SourcePath!;
    }

    public static FolderRestorePreview PreviewFolderRestore(string projectFolder)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(MasterPath))
            throw new InvalidOperationException("Configure and accept Original Game Files before restoring a folder.");
        if (!Project_Service.Instance.IsProjectLoaded)
            throw new InvalidOperationException("Load an editing project before restoring a folder.");
        string projectRoot = NormalizeMasterPath(Project_Service.Instance.ProjectPath);
        string selected = NormalizeMasterPath(projectFolder);
        string relative = Path.GetRelativePath(projectRoot, selected);
        if (relative == "." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Select an individual subfolder inside the active project Master folder.");
        string source = Path.GetFullPath(Path.Combine(MasterPath, relative));
        if (!Directory.Exists(source))
            throw new InvalidOperationException($"The Original Game Files do not contain the matching folder:{Environment.NewLine}{source}");
        var files = new List<RecoveryFileVerification>();
        foreach (string sourceFile in EnumerateRegularFiles(source))
        {
            string sourceRelative = NormalizeRelative(Path.GetRelativePath(MasterPath, sourceFile));
            files.Add(VerifyProjectFile(Path.Combine(projectRoot, sourceRelative.Replace('/', Path.DirectorySeparatorChar))));
        }
        if (files.Count == 0) throw new InvalidOperationException("The matching original folder contains no files.");
        return new(source, NormalizeRelative(relative), files.Count,
            files.Count(file => file.Trust == RecoveryFileTrust.Verified),
            files.Count(file => file.Trust == RecoveryFileTrust.Unrecognized),
            files.Count(file => file.Trust == RecoveryFileTrust.NotInManifest), files);
    }

    public static FolderRestoreResult RestoreFolder(
        string projectFolder,
        IReadOnlyCollection<string> approvedUnverifiedPaths)
    {
        FolderRestorePreview preview = PreviewFolderRestore(projectFolder);
        string destination = NormalizeMasterPath(projectFolder);
        var replacements = new List<FileReplacement>(preview.Files.Count);
        foreach (RecoveryFileVerification file in preview.Files)
        {
            // Re-hash immediately before staging so a source changed after the
            // confirmation preview cannot silently inherit its earlier trust.
            string projectFile = Path.Combine(
                Project_Service.Instance.ProjectPath!,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            RecoveryFileVerification current = VerifyProjectFile(projectFile);
            bool explicitlyApproved = approvedUnverifiedPaths.Any(path =>
                string.Equals(NormalizeRelative(path), current.RelativePath, StringComparison.OrdinalIgnoreCase));
            EnsureRestoreAllowed(current, explicitlyApproved);
            string relative = Path.GetRelativePath(preview.SourceFolder, current.SourcePath!);
            string target = Path.GetFullPath(Path.Combine(destination, relative));
            string targetPrefix = destination + Path.DirectorySeparatorChar;
            if (!target.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A Recovery source path escaped the selected project folder.");
            replacements.Add(new FileReplacement(target, File.ReadAllBytes(current.SourcePath!)));
        }
        MultiFileSaveTransaction.Save(replacements);
        return new(preview.RelativeFolder, replacements.Count);
    }

    private static void EnsureRestoreAllowed(RecoveryFileVerification file, bool allowUnverified)
    {
        if (file.Trust == RecoveryFileTrust.Verified)
            return;
        if (allowUnverified && file.CanRestore)
            return;
        string reason = file.Trust switch
        {
            RecoveryFileTrust.Unrecognized => "does not match the trusted original-file manifest",
            RecoveryFileTrust.NotInManifest => "is not included in the trusted original-file manifest",
            RecoveryFileTrust.Missing => "is missing from the configured Original Game Files",
            RecoveryFileTrust.Unreadable => "could not be read from the configured Original Game Files",
            _ => "is not authorized for Recovery"
        };
        throw new InvalidOperationException($"Recovery refused '{file.RelativePath}' because it {reason}.");
    }

    private static bool TryLoadTrustedManifest(out TrustedManifest? manifest, out string problem)
    {
        lock (Sync)
        {
            if (_manifestLoadAttempted) { manifest = _manifest; problem = _manifestProblem ?? ""; return manifest is not null; }
            _manifestLoadAttempted = true;
            try
            {
                if (!File.Exists(ManifestPath)) throw new FileNotFoundException("The packaged trusted hash manifest could not be found.");
                _manifest = JsonSerializer.Deserialize<TrustedManifest>(File.ReadAllText(ManifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (_manifest is null || _manifest.ManifestVersion is < 1 or > 2 || _manifest.Files.Count == 0 || _manifest.FileCount != _manifest.Files.Count)
                    throw new InvalidDataException("The packaged trusted hash manifest is invalid.");
                _manifestByPath = _manifest.Files.ToDictionary(file => NormalizeRelative(file.Path), StringComparer.OrdinalIgnoreCase);
                _manifestProblem = ""; manifest = _manifest; problem = ""; return true;
            }
            catch (Exception ex)
            {
                _manifest = null; _manifestByPath = null;
                _manifestProblem = "The packaged trusted hash manifest could not be used: " + ex.Message;
                manifest = null; problem = _manifestProblem; return false;
            }
        }
    }

    private static bool TryGetManifestFile(string relative, out TrustedManifestFile? expected)
    {
        if (!TryLoadTrustedManifest(out _, out _) || _manifestByPath is null) { expected = null; return false; }
        return _manifestByPath.TryGetValue(NormalizeRelative(relative), out expected);
    }

    private static IEnumerable<string> EnumerateRegularFiles(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(file); } catch { continue; }
            if ((attributes & FileAttributes.ReparsePoint) == 0) yield return file;
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
        return new(RecoverySourceState.Invalid, "Invalid Source", BuildFailureSummary(problems), problems, [], 0, 0, null,
            Fingerprint("invalid", problems));
    }

    private static string BuildFailureSummary(IReadOnlyList<string> problems)
    {
        if (problems.Count == 0) return "The selected folder is not a valid recovery source.";
        string details = string.Join(Environment.NewLine, problems.Take(8).Select(problem => "• " + problem));
        if (problems.Count > 8) details += $"{Environment.NewLine}• …and {problems.Count - 8} more problem(s).";
        return $"Validation found {problems.Count} structural problem(s):{Environment.NewLine}{details}";
    }

    private static string BuildDifferenceSummary(IReadOnlyList<RecoveryFileDifference> differences)
    {
        string details = string.Join(Environment.NewLine, differences.Take(8).Select(difference => "• " + DescribeDifference(difference)));
        if (differences.Count > 8) details += $"{Environment.NewLine}• …and {differences.Count - 8} more difference(s).";
        return $"Verification found {differences.Count} difference(s):{Environment.NewLine}{details}";
    }

    private static string DescribeDifference(RecoveryFileDifference difference) => difference.DifferenceType switch
    {
        RecoveryDifferenceType.Missing => $"Missing: {difference.RelativePath}",
        RecoveryDifferenceType.SizeMismatch => $"Size differs: {difference.RelativePath} (expected {difference.ExpectedSize}, found {difference.ActualSize})",
        RecoveryDifferenceType.HashMismatch => $"Contents differ: {difference.RelativePath}",
        RecoveryDifferenceType.Unreadable => $"Could not read/hash: {difference.RelativePath}",
        RecoveryDifferenceType.ExtraFile => $"Extra file: {difference.RelativePath}",
        _ => difference.RelativePath
    };

    private static string BuildVerificationFingerprint(string referenceId, IReadOnlyList<RecoveryFileDifference> differences) =>
        Fingerprint(referenceId, differences.OrderBy(difference => difference.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(difference => string.Join("|", difference.RelativePath, difference.DifferenceType,
                difference.ExpectedSize, difference.ActualSize, difference.ExpectedSha256, difference.ActualSha256)));

    private static string Fingerprint(string prefix, IEnumerable<string> values)
    {
        string data = prefix + "\n" + string.Join("\n", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string? LoadSavedPath()
    {
        try
        {
            string? path = AppSettings_Service.Current.OriginalGameFiles.MasterPath;
            return string.IsNullOrWhiteSpace(path) ? null : NormalizeMasterPath(path);
        }
        catch { return null; }
    }
}
