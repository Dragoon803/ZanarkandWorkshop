using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public static class SphereGridParser
{
    public const int HeaderSize = 0x10;
    public const int ClusterSize = 0x10;
    public const int NodeSize = 0x0C;
    public const int LinkSize = 0x08;
    public const int ContentHeaderSize = 0x08;

    public static SphereGridFile Read(SphereGridFileSet files) =>
        Read(
            File.ReadAllBytes(files.LayoutPath),
            File.ReadAllBytes(files.ContentPath),
            files.Kind,
            files.LayoutPath,
            files.ContentPath);

    public static SphereGridFile Read(
        byte[] layoutBytes,
        byte[] contentBytes,
        SphereGridKind kind,
        string layoutPath = "",
        string contentPath = "")
    {
        ArgumentNullException.ThrowIfNull(layoutBytes);
        ArgumentNullException.ThrowIfNull(contentBytes);
        RequireRange(layoutBytes, 0, HeaderSize, "sphere-grid header", layoutPath);
        RequireRange(contentBytes, 0, ContentHeaderSize, "node-content header", contentPath);

        ushort headerValue = ReadUInt16(layoutBytes, 0);
        ushort clusterCount = ReadUInt16(layoutBytes, 2);
        ushort nodeCount = ReadUInt16(layoutBytes, 4);
        ushort linkCount = ReadUInt16(layoutBytes, 6);
        ushort contentNodeCount = ReadUInt16(contentBytes, 2);
        if (contentNodeCount != nodeCount)
        {
            throw new InvalidDataException(
                $"Sphere-grid layout declares {nodeCount} nodes, but the node-content header " +
                $"declares {contentNodeCount}.");
        }

        SphereGridValidator.ValidateCapacities(clusterCount, nodeCount, linkCount);
        int expectedLayoutLength = checked(
            HeaderSize +
            clusterCount * ClusterSize +
            nodeCount * NodeSize +
            linkCount * LinkSize);
        if (layoutBytes.Length != expectedLayoutLength)
        {
            throw new InvalidDataException(
                $"Sphere-grid layout length is 0x{layoutBytes.Length:X}; counts require exactly " +
                $"0x{expectedLayoutLength:X} bytes.");
        }

        int expectedContentLength = checked(ContentHeaderSize + nodeCount);
        if (contentBytes.Length < expectedContentLength)
        {
            throw new InvalidDataException(
                $"Node-content file is 0x{contentBytes.Length:X} bytes; {nodeCount} nodes require " +
                $"at least 0x{expectedContentLength:X} bytes.");
        }

        var clusters = new List<SphereGridCluster>(clusterCount);
        int clusterBase = HeaderSize;
        for (int index = 0; index < clusterCount; index++)
        {
            int offset = clusterBase + index * ClusterSize;
            clusters.Add(new SphereGridCluster(
                index,
                offset,
                ReadInt16(layoutBytes, offset),
                ReadInt16(layoutBytes, offset + 2),
                ReadUInt16(layoutBytes, offset + 4),
                ReadUInt16(layoutBytes, offset + 6),
                ReadUInt16(layoutBytes, offset + 8),
                ReadUInt16(layoutBytes, offset + 10),
                ReadUInt16(layoutBytes, offset + 12),
                ReadUInt16(layoutBytes, offset + 14)));
        }

        var nodes = new List<SphereGridNode>(nodeCount);
        int nodeBase = clusterBase + clusterCount * ClusterSize;
        for (int index = 0; index < nodeCount; index++)
        {
            int offset = nodeBase + index * NodeSize;
            nodes.Add(new SphereGridNode(
                index,
                offset,
                ReadInt16(layoutBytes, offset),
                ReadInt16(layoutBytes, offset + 2),
                ReadUInt16(layoutBytes, offset + 4),
                ReadUInt16(layoutBytes, offset + 6),
                ReadUInt16(layoutBytes, offset + 8),
                ReadUInt16(layoutBytes, offset + 10),
                contentBytes[ContentHeaderSize + index]));
        }

        var links = new List<SphereGridLink>(linkCount);
        int linkBase = nodeBase + nodeCount * NodeSize;
        for (int index = 0; index < linkCount; index++)
        {
            int offset = linkBase + index * LinkSize;
            links.Add(new SphereGridLink(
                index,
                offset,
                ReadUInt16(layoutBytes, offset),
                ReadUInt16(layoutBytes, offset + 2),
                ReadUInt16(layoutBytes, offset + 4),
                ReadUInt16(layoutBytes, offset + 6)));
        }

        var result = new SphereGridFile
        {
            Kind = kind,
            LayoutPath = layoutPath,
            ContentPath = contentPath,
            OriginalLayoutBytes = (byte[])layoutBytes.Clone(),
            OriginalContentBytes = (byte[])contentBytes.Clone(),
            HeaderValue = headerValue,
            UnknownHeaderValues =
            [
                ReadUInt16(layoutBytes, 8),
                ReadUInt16(layoutBytes, 10),
                ReadUInt16(layoutBytes, 12),
                ReadUInt16(layoutBytes, 14)
            ],
            Clusters = clusters,
            Nodes = nodes,
            Links = links
        };
        SphereGridValidator.ValidateReferences(result);
        return result;
    }

    private static short ReadInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    private static void RequireRange(
        byte[] bytes, int offset, int length, string label, string path)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            string source = string.IsNullOrWhiteSpace(path) ? "file" : path;
            throw new InvalidDataException(
                $"{label} requires 0x{length:X} bytes at 0x{offset:X}, outside {source} " +
                $"(0x{bytes.Length:X} bytes).");
        }
    }
}
