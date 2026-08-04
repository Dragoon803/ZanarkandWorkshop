using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public enum SphereGridKind
{
    Original,
    Standard,
    Expert
}

public sealed record SphereGridFileSet(
    SphereGridKind Kind,
    string LayoutPath,
    string ContentPath)
{
    public static SphereGridFileSet FromDirectory(string directory, SphereGridKind kind)
    {
        int layoutNumber = kind switch
        {
            SphereGridKind.Original => 1,
            SphereGridKind.Standard => 2,
            SphereGridKind.Expert => 3,
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind))
        };
        return new SphereGridFileSet(
            kind,
            Path.Combine(directory, $"dat{layoutNumber:D2}.dat"),
            Path.Combine(directory, $"dat{layoutNumber + 8:D2}.dat"));
    }
}

public sealed record SphereGridCluster(
    int Index,
    int FileOffset,
    short X,
    short Y,
    ushort Unknown04,
    ushort Type,
    ushort Unknown08,
    ushort Unknown0A,
    ushort Unknown0C,
    ushort Unknown0E)
{
    public int SizeClass => Type & 0x03;
    public bool UsesAlternateDesign => (Type & 0x04) != 0;
}

public sealed record SphereGridNode(
    int Index,
    int FileOffset,
    short X,
    short Y,
    ushort Unknown04,
    ushort RedundantType,
    ushort ClusterIndex,
    ushort Unknown0A,
    byte Type)
{
    public SphereGridNodeTypeInfo TypeInfo => SphereGridNodeTypes.Get(Type);
    public bool ContentMatchesLayout => RedundantType == Type;
    public bool IsVisible => Type != byte.MaxValue && RedundantType != ushort.MaxValue;
}

public sealed record SphereGridLink(
    int Index,
    int FileOffset,
    ushort NodeAIndex,
    ushort NodeBIndex,
    ushort AnchorNodeIndex,
    ushort Unknown06)
{
    public bool IsCurved => AnchorNodeIndex != ushort.MaxValue;
}

public sealed class SphereGridFile
{
    public required SphereGridKind Kind { get; init; }
    public required string LayoutPath { get; init; }
    public required string ContentPath { get; init; }
    public required byte[] OriginalLayoutBytes { get; init; }
    public required byte[] OriginalContentBytes { get; init; }
    public required ushort HeaderValue { get; init; }
    public required ushort[] UnknownHeaderValues { get; init; }
    public required IReadOnlyList<SphereGridCluster> Clusters { get; init; }
    public required IReadOnlyList<SphereGridNode> Nodes { get; init; }
    public required IReadOnlyList<SphereGridLink> Links { get; init; }
}
