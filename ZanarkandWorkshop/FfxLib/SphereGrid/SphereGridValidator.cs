using System.IO;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public static class SphereGridValidator
{
    public const int MaximumClusters = 128;
    public const int MaximumNodes = 1024;
    public const int MaximumLinks = 1024;

    public static void ValidateCapacities(
        int clusterCount, int nodeCount, int linkCount)
    {
        if (clusterCount > MaximumClusters)
            throw new InvalidDataException(
                $"Sphere grid has {clusterCount} clusters; the runtime capacity is {MaximumClusters}.");
        if (nodeCount > MaximumNodes)
            throw new InvalidDataException(
                $"Sphere grid has {nodeCount} nodes; the runtime capacity is {MaximumNodes}.");
        if (linkCount > MaximumLinks)
            throw new InvalidDataException(
                $"Sphere grid has {linkCount} links; the runtime capacity is {MaximumLinks}.");
    }

    public static void ValidateReferences(SphereGridFile file)
    {
        ValidateCapacities(file.Clusters.Count, file.Nodes.Count, file.Links.Count);

        foreach (SphereGridCluster cluster in file.Clusters)
        {
            if (cluster.Type > 7)
            {
                throw new InvalidDataException(
                    $"Cluster {cluster.Index} has visual type {cluster.Type}; valid types are 0 through 7.");
            }
        }

        foreach (SphereGridNode node in file.Nodes)
        {
            if (node.ClusterIndex >= file.Clusters.Count)
            {
                throw new InvalidDataException(
                    $"Node {node.Index} references cluster {node.ClusterIndex}, but the grid has " +
                    $"{file.Clusters.Count} clusters.");
            }
        }

        var linkCounts = new int[file.Nodes.Count];
        foreach (SphereGridLink link in file.Links)
        {
            if (link.NodeAIndex >= file.Nodes.Count)
                throw InvalidLinkReference(link.Index, "first endpoint", link.NodeAIndex, file.Nodes.Count);
            if (link.NodeBIndex >= file.Nodes.Count)
                throw InvalidLinkReference(link.Index, "second endpoint", link.NodeBIndex, file.Nodes.Count);
            if (link.IsCurved && link.AnchorNodeIndex >= file.Nodes.Count)
                throw InvalidLinkReference(link.Index, "anchor", link.AnchorNodeIndex, file.Nodes.Count);
            if (link.NodeAIndex == link.NodeBIndex)
                throw new InvalidDataException(
                    $"Link {link.Index} uses node {link.NodeAIndex} as both endpoints.");
            linkCounts[link.NodeAIndex]++;
            linkCounts[link.NodeBIndex]++;
        }

        for (int nodeIndex = 0; nodeIndex < linkCounts.Length; nodeIndex++)
            if (linkCounts[nodeIndex] > 5)
                throw new InvalidDataException(
                    $"Node {nodeIndex} has {linkCounts[nodeIndex]} links; the runtime node structure supports 5.");
    }

    private static InvalidDataException InvalidLinkReference(
        int linkIndex, string field, ushort value, int nodeCount) =>
        new(
            $"Link {linkIndex} references node {value} as its {field}, but the grid has " +
            $"{nodeCount} nodes.");
}
