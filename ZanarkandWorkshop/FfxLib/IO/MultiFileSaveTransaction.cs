using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.IO;

public sealed record FileReplacement(string DestinationPath, byte[] ReplacementBytes);

/// <summary>
/// Stages and verifies a set of replacements before installing any of them.
/// If installation fails, every installed destination is restored (or removed
/// when it did not exist before the transaction).
/// </summary>
public static class MultiFileSaveTransaction
{
    internal static Action<int>? BeforeInstallForTesting { get; set; }

    public static void Save(IReadOnlyList<FileReplacement> replacements) =>
        Save(replacements, BeforeInstallForTesting);

    internal static void Save(
        IReadOnlyList<FileReplacement> replacements,
        Action<int>? beforeInstall)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count == 0)
            throw new ArgumentException("At least one file replacement is required.", nameof(replacements));

        string transactionId = Guid.NewGuid().ToString("N");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileState>(replacements.Count);
        foreach (FileReplacement replacement in replacements)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(replacement.DestinationPath);
            ArgumentNullException.ThrowIfNull(replacement.ReplacementBytes);
            string path = Path.GetFullPath(replacement.DestinationPath);
            if (!paths.Add(path))
                throw new InvalidOperationException($"The transaction contains duplicate destination '{path}'.");
            string directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("A replacement path has no parent directory.");
            string name = Path.GetFileName(path);
            files.Add(new FileState(path, replacement.ReplacementBytes,
                Path.Combine(directory, $".{name}.zwstage-{transactionId}"),
                Path.Combine(directory, $".{name}.zwrollback-{transactionId}")));
        }

        try
        {
            foreach (FileState file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                file.OriginalExisted = File.Exists(file.Path);
                file.OriginalBytes = file.OriginalExisted ? File.ReadAllBytes(file.Path) : [];
                File.WriteAllBytes(file.StagedPath, file.OutputBytes);
                if (file.OriginalExisted)
                    File.WriteAllBytes(file.RollbackPath, file.OriginalBytes);

                VerifyBytes(file.StagedPath, file.OutputBytes, "staged replacement");
                if (file.OriginalExisted)
                    VerifyBytes(file.RollbackPath, file.OriginalBytes, "rollback copy");
            }

            try
            {
                for (int index = 0; index < files.Count; index++)
                {
                    beforeInstall?.Invoke(index);
                    File.Move(files[index].StagedPath, files[index].Path, true);
                    files[index].Installed = true;
                }
            }
            catch (Exception installError)
            {
                var rollbackErrors = new List<Exception>();
                for (int index = files.Count - 1; index >= 0; index--)
                {
                    FileState file = files[index];
                    if (!file.Installed) continue;
                    try
                    {
                        if (file.OriginalExisted)
                            File.Move(file.RollbackPath, file.Path, true);
                        else if (File.Exists(file.Path))
                            File.Delete(file.Path);
                        file.Installed = false;
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackErrors.Add(new IOException($"Could not restore '{file.Path}'.", rollbackError));
                    }
                }

                if (rollbackErrors.Count != 0)
                    throw new AggregateException(
                        "The file transaction failed and automatic rollback was incomplete. " +
                        "Use Recovery before continuing to edit this project.",
                        new[] { installError }.Concat(rollbackErrors));

                throw new IOException(
                    "The file transaction failed. All destinations were restored to their pre-operation contents.",
                    installError);
            }
        }
        finally
        {
            foreach (FileState file in files)
            {
                TryDelete(file.StagedPath);
                TryDelete(file.RollbackPath);
            }
        }
    }

    private static void VerifyBytes(string path, byte[] expected, string description)
    {
        if (!File.ReadAllBytes(path).SequenceEqual(expected))
            throw new InvalidDataException($"The {description} for '{Path.GetFileName(path)}' did not verify byte-for-byte.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private sealed class FileState(string path, byte[] outputBytes, string stagedPath, string rollbackPath)
    {
        public string Path { get; } = path;
        public byte[] OutputBytes { get; } = outputBytes;
        public string StagedPath { get; } = stagedPath;
        public string RollbackPath { get; } = rollbackPath;
        public bool OriginalExisted { get; set; }
        public byte[] OriginalBytes { get; set; } = [];
        public bool Installed { get; set; }
    }
}
