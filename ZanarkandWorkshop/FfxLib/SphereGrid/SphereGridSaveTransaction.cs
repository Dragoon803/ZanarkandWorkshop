using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public static class SphereGridSaveTransaction
{
    public static SphereGridFile Save(SphereGridFile source, SphereGridWriteResult output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        if (string.IsNullOrWhiteSpace(source.LayoutPath) ||
            string.IsNullOrWhiteSpace(source.ContentPath))
            throw new InvalidOperationException("Sphere Grid source paths are required for saving.");

        // Validate both buffers together before creating any files.
        SphereGridFile verified = SphereGridParser.Read(
            output.LayoutBytes,
            output.ContentBytes,
            source.Kind,
            source.LayoutPath,
            source.ContentPath);

        string layoutTemporary = source.LayoutPath + ".zwtmp";
        string contentTemporary = source.ContentPath + ".zwtmp";

        EnsureDistinctPaths(
            source.LayoutPath, source.ContentPath,
            layoutTemporary, contentTemporary);

        try
        {
            File.WriteAllBytes(layoutTemporary, output.LayoutBytes);
            File.WriteAllBytes(contentTemporary, output.ContentBytes);

            // Verify the actual staged files, not only the in-memory buffers.
            _ = SphereGridParser.Read(new SphereGridFileSet(
                source.Kind, layoutTemporary, contentTemporary));

            // Keep the originals only in memory for transaction rollback. Persistent
            // restoration is handled by the application's Recovery feature.
            byte[] originalLayout = File.ReadAllBytes(source.LayoutPath);
            byte[] originalContent = File.ReadAllBytes(source.ContentPath);

            bool layoutInstalled = false;
            bool contentInstalled = false;
            try
            {
                File.Move(layoutTemporary, source.LayoutPath, true);
                layoutInstalled = true;
                File.Move(contentTemporary, source.ContentPath, true);
                contentInstalled = true;
            }
            catch (Exception saveError)
            {
                try
                {
                    if (layoutInstalled)
                        File.WriteAllBytes(source.LayoutPath, originalLayout);
                    if (contentInstalled)
                        File.WriteAllBytes(source.ContentPath, originalContent);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Sphere Grid saving failed and its automatic rollback also failed. " +
                        "Use Recovery to restore the original Sphere Grid files.",
                        saveError,
                        rollbackError);
                }
                throw new IOException(
                    "Sphere Grid saving failed. The project files were restored to their " +
                    "pre-save contents.",
                    saveError);
            }

            return verified;
        }
        finally
        {
            TryDelete(layoutTemporary);
            TryDelete(contentTemporary);
        }
    }

    private static void EnsureDistinctPaths(params string[] paths)
    {
        for (int left = 0; left < paths.Length; left++)
        {
            for (int right = left + 1; right < paths.Length; right++)
            {
                if (string.Equals(
                    Path.GetFullPath(paths[left]),
                    Path.GetFullPath(paths[right]),
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Sphere Grid save paths must be distinct.");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stale temporary file is harmless and can be replaced by the next save.
        }
    }
}
