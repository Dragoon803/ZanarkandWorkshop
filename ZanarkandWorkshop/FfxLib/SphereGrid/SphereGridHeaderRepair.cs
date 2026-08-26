using System;
using System.Buffers.Binary;
using System.IO;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public sealed record SphereGridHeaderMismatch(
    SphereGridFileSet Files,
    ushort LayoutNodeCount,
    ushort ContentNodeCount,
    int AvailableTypeBytes,
    bool CanRepair)
{
    public string Description =>
        $"{Files.Kind}: layout declares {LayoutNodeCount} nodes; " +
        $"content header declares {ContentNodeCount}; " +
        $"{AvailableTypeBytes} type bytes are present.";
}

public static class SphereGridHeaderRepair
{
    public static SphereGridHeaderMismatch? Inspect(SphereGridFileSet files)
    {
        byte[] layout = File.ReadAllBytes(files.LayoutPath);
        byte[] content = File.ReadAllBytes(files.ContentPath);
        if (layout.Length < SphereGridParser.HeaderSize)
            throw new InvalidDataException($"{files.LayoutPath} is too short to contain a Sphere Grid header.");
        if (content.Length < SphereGridParser.ContentHeaderSize)
            throw new InvalidDataException($"{files.ContentPath} is too short to contain a node-content header.");

        ushort layoutCount = BinaryPrimitives.ReadUInt16LittleEndian(layout.AsSpan(4, 2));
        ushort contentCount = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(2, 2));
        if (layoutCount == contentCount)
            return null;

        int availableTypeBytes = content.Length - SphereGridParser.ContentHeaderSize;
        bool canRepair = availableTypeBytes == layoutCount &&
                         layoutCount <= SphereGridValidator.MaximumNodes;
        return new SphereGridHeaderMismatch(
            files, layoutCount, contentCount, availableTypeBytes, canRepair);
    }

    public static void Repair(SphereGridHeaderMismatch mismatch)
    {
        ArgumentNullException.ThrowIfNull(mismatch);
        if (!mismatch.CanRepair)
            throw new InvalidDataException(
                "The Sphere Grid header mismatch cannot be repaired automatically because " +
                "the available node-type data does not exactly match the layout node count.");

        SphereGridHeaderMismatch? current = Inspect(mismatch.Files);
        if (current is null)
            return;
        if (current.LayoutNodeCount != mismatch.LayoutNodeCount ||
            current.ContentNodeCount != mismatch.ContentNodeCount ||
            !current.CanRepair)
            throw new InvalidOperationException(
                "The Sphere Grid files changed after the repair warning was displayed. Open the editor again.");

        byte[] layout = File.ReadAllBytes(mismatch.Files.LayoutPath);
        byte[] repairedContent = File.ReadAllBytes(mismatch.Files.ContentPath);
        BinaryPrimitives.WriteUInt16LittleEndian(
            repairedContent.AsSpan(2, 2), mismatch.LayoutNodeCount);

        _ = SphereGridParser.Read(
            layout,
            repairedContent,
            mismatch.Files.Kind,
            mismatch.Files.LayoutPath,
            mismatch.Files.ContentPath);

        string temporary = mismatch.Files.ContentPath + ".zwtmp";
        try
        {
            File.WriteAllBytes(temporary, repairedContent);
            _ = SphereGridParser.Read(new SphereGridFileSet(
                mismatch.Files.Kind,
                mismatch.Files.LayoutPath,
                temporary));
            File.Move(temporary, mismatch.Files.ContentPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
                // A stale temporary file is safe to replace during the next repair.
            }
        }
    }
}
