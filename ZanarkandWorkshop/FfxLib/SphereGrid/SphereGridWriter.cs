using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public sealed record SphereGridWriteResult(byte[] LayoutBytes, byte[] ContentBytes);

public static class SphereGridWriter
{
    public static SphereGridWriteResult Write(
        SphereGridFile source,
        IReadOnlyList<SphereGridCluster> clusters,
        IReadOnlyList<SphereGridNode> nodes,
        IReadOnlyList<SphereGridLink> links)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        if (clusters.Count != source.Clusters.Count ||
            nodes.Count != source.Nodes.Count ||
            links.Count != source.Links.Count)
            return WriteResized(source, clusters, nodes, links);
        RequireSameCount("clusters", source.Clusters.Count, clusters.Count);
        RequireSameCount("nodes", source.Nodes.Count, nodes.Count);
        RequireSameCount("links", source.Links.Count, links.Count);

        byte[] layout = (byte[])source.OriginalLayoutBytes.Clone();
        byte[] content = (byte[])source.OriginalContentBytes.Clone();

        for (int index = 0; index < clusters.Count; index++)
        {
            SphereGridCluster original = source.Clusters[index];
            SphereGridCluster edited = clusters[index];
            ValidateIdentity("Cluster", index, original.FileOffset, edited.Index, edited.FileOffset);
            if (edited.Type > 7)
                throw new InvalidDataException(
                    $"Cluster {index} has visual type {edited.Type}; valid types are 0 through 7.");
            if (edited.Unknown04 != original.Unknown04 ||
                edited.Unknown08 != original.Unknown08 ||
                edited.Unknown0A != original.Unknown0A ||
                edited.Unknown0C != original.Unknown0C ||
                edited.Unknown0E != original.Unknown0E)
                throw UnsupportedRawEdit("Cluster", index);
            WriteInt16(layout, original.FileOffset, edited.X);
            WriteInt16(layout, original.FileOffset + 2, edited.Y);
            WriteUInt16(layout, original.FileOffset + 6, edited.Type);
        }

        for (int index = 0; index < nodes.Count; index++)
        {
            SphereGridNode original = source.Nodes[index];
            SphereGridNode edited = nodes[index];
            ValidateIdentity("Node", index, original.FileOffset, edited.Index, edited.FileOffset);
            if (edited.Unknown04 != original.Unknown04 ||
                edited.Unknown0A != original.Unknown0A ||
                (edited.Type == original.Type &&
                 edited.RedundantType != original.RedundantType))
                throw UnsupportedRawEdit("Node", index);
            if (edited.ClusterIndex >= clusters.Count)
            {
                throw new InvalidDataException(
                    $"Node {index} references cluster {edited.ClusterIndex}, but the grid has " +
                    $"{clusters.Count} clusters.");
            }

            WriteInt16(layout, original.FileOffset, edited.X);
            WriteInt16(layout, original.FileOffset + 2, edited.Y);
            WriteUInt16(layout, original.FileOffset + 8, edited.ClusterIndex);

            // dat09/10/11 is authoritative. Only synchronize the redundant layout value
            // when the user actually changes a node type, preserving existing Expert-grid
            // mismatches on untouched nodes.
            if (edited.Type != original.Type)
            {
                content[SphereGridParser.ContentHeaderSize + index] = edited.Type;
                WriteUInt16(layout, original.FileOffset + 6, edited.Type);
            }
        }

        for (int index = 0; index < links.Count; index++)
        {
            SphereGridLink original = source.Links[index];
            SphereGridLink edited = links[index];
            ValidateIdentity("Link", index, original.FileOffset, edited.Index, edited.FileOffset);
            if (edited.Unknown06 != original.Unknown06)
                throw UnsupportedRawEdit("Link", index);
            WriteUInt16(layout, original.FileOffset, edited.NodeAIndex);
            WriteUInt16(layout, original.FileOffset + 2, edited.NodeBIndex);
            WriteUInt16(layout, original.FileOffset + 4, edited.AnchorNodeIndex);
        }

        // Generated output must satisfy the same structural checks as source data before
        // it can reach a project file.
        _ = SphereGridParser.Read(
            layout, content, source.Kind, source.LayoutPath, source.ContentPath);
        return new SphereGridWriteResult(layout, content);
    }

    private static SphereGridWriteResult WriteResized(
        SphereGridFile source,
        IReadOnlyList<SphereGridCluster> clusters,
        IReadOnlyList<SphereGridNode> nodes,
        IReadOnlyList<SphereGridLink> links)
    {
        if (clusters.Count != source.Clusters.Count)
            throw new InvalidDataException("Structure editing does not support adding or removing clusters.");
        if (nodes.Count < source.Nodes.Count || links.Count < source.Links.Count)
            throw new InvalidDataException("Structure editing currently supports append-only nodes and links.");
        SphereGridValidator.ValidateCapacities(clusters.Count, nodes.Count, links.Count);

        int layoutLength = checked(
            SphereGridParser.HeaderSize +
            clusters.Count * SphereGridParser.ClusterSize +
            nodes.Count * SphereGridParser.NodeSize +
            links.Count * SphereGridParser.LinkSize);
        byte[] layout = new byte[layoutLength];
        WriteUInt16(layout, 0, source.HeaderValue);
        WriteUInt16(layout, 2, checked((ushort)clusters.Count));
        WriteUInt16(layout, 4, checked((ushort)nodes.Count));
        WriteUInt16(layout, 6, checked((ushort)links.Count));
        for (int index = 0; index < source.UnknownHeaderValues.Length && index < 4; index++)
            WriteUInt16(layout, 8 + index * 2, source.UnknownHeaderValues[index]);

        int clusterBase = SphereGridParser.HeaderSize;
        for (int index = 0; index < clusters.Count; index++)
        {
            SphereGridCluster cluster = clusters[index];
            if (cluster.Index != index || cluster.Type > 7)
                throw new InvalidDataException($"Cluster {index} is not valid for rebuilding.");
            int offset = clusterBase + index * SphereGridParser.ClusterSize;
            WriteInt16(layout, offset, cluster.X);
            WriteInt16(layout, offset + 2, cluster.Y);
            WriteUInt16(layout, offset + 4, cluster.Unknown04);
            WriteUInt16(layout, offset + 6, cluster.Type);
            WriteUInt16(layout, offset + 8, cluster.Unknown08);
            WriteUInt16(layout, offset + 10, cluster.Unknown0A);
            WriteUInt16(layout, offset + 12, cluster.Unknown0C);
            WriteUInt16(layout, offset + 14, cluster.Unknown0E);
        }

        int nodeBase = clusterBase + clusters.Count * SphereGridParser.ClusterSize;
        for (int index = 0; index < nodes.Count; index++)
        {
            SphereGridNode node = nodes[index];
            if (node.Index != index || node.ClusterIndex >= clusters.Count)
                throw new InvalidDataException($"Node {index} is not valid for rebuilding.");
            int offset = nodeBase + index * SphereGridParser.NodeSize;
            WriteInt16(layout, offset, node.X);
            WriteInt16(layout, offset + 2, node.Y);
            WriteUInt16(layout, offset + 4, node.Unknown04);
            ushort redundantType = index < source.Nodes.Count && node.Type == source.Nodes[index].Type
                ? node.RedundantType
                : node.Type;
            WriteUInt16(layout, offset + 6, redundantType);
            WriteUInt16(layout, offset + 8, node.ClusterIndex);
            WriteUInt16(layout, offset + 10, node.Unknown0A);
        }

        int linkBase = nodeBase + nodes.Count * SphereGridParser.NodeSize;
        for (int index = 0; index < links.Count; index++)
        {
            SphereGridLink link = links[index];
            if (link.Index != index)
                throw new InvalidDataException($"Link {index} is not valid for rebuilding.");
            int offset = linkBase + index * SphereGridParser.LinkSize;
            WriteUInt16(layout, offset, link.NodeAIndex);
            WriteUInt16(layout, offset + 2, link.NodeBIndex);
            WriteUInt16(layout, offset + 4, link.AnchorNodeIndex);
            WriteUInt16(layout, offset + 6, link.Unknown06);
        }

        int originalTableLength = SphereGridParser.ContentHeaderSize + source.Nodes.Count;
        int trailingLength = Math.Max(0, source.OriginalContentBytes.Length - originalTableLength);
        byte[] content = new byte[SphereGridParser.ContentHeaderSize + nodes.Count + trailingLength];
        Array.Copy(source.OriginalContentBytes, 0, content, 0, SphereGridParser.ContentHeaderSize);
        WriteUInt16(content, 2, checked((ushort)nodes.Count));
        for (int index = 0; index < nodes.Count; index++)
            content[SphereGridParser.ContentHeaderSize + index] = nodes[index].Type;
        if (trailingLength > 0)
        {
            Array.Copy(
                source.OriginalContentBytes,
                originalTableLength,
                content,
                SphereGridParser.ContentHeaderSize + nodes.Count,
                trailingLength);
        }

        _ = SphereGridParser.Read(layout, content, source.Kind, source.LayoutPath, source.ContentPath);
        return new SphereGridWriteResult(layout, content);
    }

    public static SphereGridWriteResult Write(SphereGridFile source) =>
        Write(source, source.Clusters, source.Nodes, source.Links);

    private static void RequireSameCount(string label, int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Fixed-size Sphere Grid saving requires exactly {expected} {label}; received {actual}.");
        }
    }

    private static void ValidateIdentity(
        string label, int expectedIndex, int expectedOffset, int actualIndex, int actualOffset)
    {
        if (actualIndex != expectedIndex || actualOffset != expectedOffset)
        {
            throw new InvalidDataException(
                $"{label} {expectedIndex} no longer maps to its original file record.");
        }
    }

    private static InvalidDataException UnsupportedRawEdit(string label, int index) =>
        new($"{label} {index} contains changes to fields that are not safely editable yet.");

    private static void WriteInt16(byte[] bytes, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
}
