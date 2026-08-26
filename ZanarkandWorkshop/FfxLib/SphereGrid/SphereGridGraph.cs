using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public readonly record struct SphereGridBounds(
    double MinimumX,
    double MinimumY,
    double MaximumX,
    double MaximumY)
{
    public double Width => MaximumX - MinimumX;
    public double Height => MaximumY - MinimumY;
    public bool IsEmpty => Width <= 0 && Height <= 0;
}

public sealed class SphereGridGraph
{
    private readonly IReadOnlyList<int>[] _linkIndicesByNode;

    public SphereGridFile File { get; }
    public SphereGridBounds Bounds { get; }
    public SphereGridRouteMetadata Routes { get; }
    public IReadOnlyList<SphereGridNode> VisibleNodes { get; }

    public SphereGridGraph(
        SphereGridFile file,
        SphereGridRouteMetadata? routes = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        SphereGridValidator.ValidateReferences(file);
        File = file;
        _linkIndicesByNode = new IReadOnlyList<int>[file.Nodes.Count];
        var mutableAdjacency = new List<int>[file.Nodes.Count];
        for (int index = 0; index < mutableAdjacency.Length; index++)
            mutableAdjacency[index] = new List<int>();
        foreach (SphereGridLink link in file.Links)
        {
            mutableAdjacency[link.NodeAIndex].Add(link.Index);
            if (link.NodeBIndex != link.NodeAIndex)
                mutableAdjacency[link.NodeBIndex].Add(link.Index);
        }
        for (int index = 0; index < mutableAdjacency.Length; index++)
            _linkIndicesByNode[index] = mutableAdjacency[index].AsReadOnly();
        var visibleNodes = new List<SphereGridNode>();
        foreach (SphereGridNode node in file.Nodes)
        {
            if (node.IsVisible)
                visibleNodes.Add(node);
        }
        VisibleNodes = visibleNodes.AsReadOnly();
        Bounds = CalculateBounds(file);
        Routes = routes ?? SphereGridRouteMetadata.Build(this);
    }

    public IReadOnlyList<int> GetLinkIndices(int nodeIndex)
    {
        if ((uint)nodeIndex >= (uint)_linkIndicesByNode.Length)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));
        return _linkIndicesByNode[nodeIndex];
    }

    public bool IsLinkConnectedTo(int linkIndex, int nodeIndex)
    {
        if ((uint)linkIndex >= (uint)File.Links.Count)
            throw new ArgumentOutOfRangeException(nameof(linkIndex));
        SphereGridLink link = File.Links[linkIndex];
        return link.NodeAIndex == nodeIndex || link.NodeBIndex == nodeIndex;
    }

    private static SphereGridBounds CalculateBounds(SphereGridFile file)
    {
        if (file.Nodes.Count == 0 && file.Clusters.Count == 0)
            return new SphereGridBounds(-1, -1, 1, 1);

        double minimumX = double.PositiveInfinity;
        double minimumY = double.PositiveInfinity;
        double maximumX = double.NegativeInfinity;
        double maximumY = double.NegativeInfinity;
        foreach (SphereGridNode node in file.Nodes)
        {
            if (node.IsVisible)
                Include(node.X, node.Y);
        }
        foreach (SphereGridCluster cluster in file.Clusters)
            Include(cluster.X, cluster.Y);
        return new SphereGridBounds(minimumX, minimumY, maximumX, maximumY);

        void Include(double x, double y)
        {
            minimumX = Math.Min(minimumX, x);
            minimumY = Math.Min(minimumY, y);
            maximumX = Math.Max(maximumX, x);
            maximumY = Math.Max(maximumY, y);
        }
    }
}
