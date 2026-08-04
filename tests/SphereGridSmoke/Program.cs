using FFXProjectEditor.FfxLib.SphereGrid;
using System.Buffers.Binary;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static (byte[] Layout, byte[] Content) MakeSyntheticGrid()
{
    const int clusters = 1;
    const int nodes = 3;
    const int links = 2;
    var layout = new byte[
        SphereGridParser.HeaderSize +
        clusters * SphereGridParser.ClusterSize +
        nodes * SphereGridParser.NodeSize +
        links * SphereGridParser.LinkSize];
    var content = new byte[SphereGridParser.ContentHeaderSize + nodes];

    WriteUInt16(layout, 0, 49);
    WriteUInt16(layout, 2, clusters);
    WriteUInt16(layout, 4, nodes);
    WriteUInt16(layout, 6, links);
    WriteUInt16(content, 0, 49);
    WriteUInt16(content, 2, nodes);

    int clusterOffset = SphereGridParser.HeaderSize;
    WriteInt16(layout, clusterOffset, -100);
    WriteInt16(layout, clusterOffset + 2, 200);
    WriteUInt16(layout, clusterOffset + 6, 5);

    int nodeOffset = clusterOffset + SphereGridParser.ClusterSize;
    WriteNode(layout, nodeOffset, -20, 30, 0x02, 0);
    WriteNode(layout, nodeOffset + SphereGridParser.NodeSize, 0, 40, 0x27, 0);
    WriteNode(layout, nodeOffset + SphereGridParser.NodeSize * 2, 20, 30, 0x39, 0);
    content[SphereGridParser.ContentHeaderSize] = 0x02;
    content[SphereGridParser.ContentHeaderSize + 1] = 0x27;
    content[SphereGridParser.ContentHeaderSize + 2] = 0x39;

    int linkOffset = nodeOffset + nodes * SphereGridParser.NodeSize;
    WriteLink(layout, linkOffset, 0, 1, ushort.MaxValue);
    WriteLink(layout, linkOffset + SphereGridParser.LinkSize, 1, 2, 0);
    return (layout, content);
}

static void WriteNode(
    byte[] bytes, int offset, short x, short y, ushort type, ushort cluster)
{
    WriteInt16(bytes, offset, x);
    WriteInt16(bytes, offset + 2, y);
    WriteUInt16(bytes, offset + 6, type);
    WriteUInt16(bytes, offset + 8, cluster);
}

static void WriteLink(
    byte[] bytes, int offset, ushort nodeA, ushort nodeB, ushort anchor)
{
    WriteUInt16(bytes, offset, nodeA);
    WriteUInt16(bytes, offset + 2, nodeB);
    WriteUInt16(bytes, offset + 4, anchor);
}

static void WriteInt16(byte[] bytes, int offset, short value) =>
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, 2), value);

static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);

(byte[] layout, byte[] content) = MakeSyntheticGrid();
SphereGridFile synthetic = SphereGridParser.Read(
    layout, content, SphereGridKind.Standard, "synthetic-layout", "synthetic-content");
Assert(synthetic.HeaderValue == 49, "Header value");
Assert(synthetic.Clusters.Count == 1, "Cluster count");
Assert(synthetic.Nodes.Count == 3, "Node count");
Assert(synthetic.Links.Count == 2, "Link count");
Assert(synthetic.Clusters[0].X == -100, "Signed cluster coordinate");
Assert(synthetic.Clusters[0].SizeClass == 1, "Cluster size class");
Assert(synthetic.Clusters[0].UsesAlternateDesign, "Alternate cluster design");
Assert(synthetic.Nodes[0].X == -20, "Signed node coordinate");
Assert(synthetic.Nodes[0].TypeInfo.ShortName == "Str+1", "Node abbreviation");
Assert(synthetic.Nodes[1].TypeInfo.ShortName == "Lock1", "Lock abbreviation");
Assert(!synthetic.Links[0].IsCurved, "Straight link");
Assert(synthetic.Links[1].IsCurved, "Curved link");
Assert(SphereGridNodeTypes.All.Count == 0x7F, "Complete known node-type catalog");
var syntheticGraph = new SphereGridGraph(synthetic);
Assert(syntheticGraph.GetLinkIndices(0).SequenceEqual(new[] { 0 }), "Node 0 adjacency");
Assert(syntheticGraph.GetLinkIndices(1).SequenceEqual(new[] { 0, 1 }), "Node 1 adjacency");
Assert(syntheticGraph.IsLinkConnectedTo(1, 2), "Connected-link query");
Assert(syntheticGraph.Bounds.MinimumX == -100, "Graph minimum X includes clusters");
Assert(syntheticGraph.Bounds.MaximumY == 200, "Graph maximum Y includes clusters");

SphereGridWriteResult unchanged = SphereGridWriter.Write(synthetic);
Assert(layout.SequenceEqual(unchanged.LayoutBytes), "Unedited layout must be byte-identical");
Assert(content.SequenceEqual(unchanged.ContentBytes), "Unedited contents must be byte-identical");

SphereGridNode[] typeEditedNodes = synthetic.Nodes.ToArray();
typeEditedNodes[0] = typeEditedNodes[0] with { Type = 0x05 };
SphereGridWriteResult typeEdited = SphereGridWriter.Write(
    synthetic, synthetic.Clusters, typeEditedNodes, synthetic.Links);
AssertChangedOnly(
    layout,
    typeEdited.LayoutBytes,
    synthetic.Nodes[0].FileOffset + 6,
    synthetic.Nodes[0].FileOffset + 7);
AssertChangedOnly(
    content,
    typeEdited.ContentBytes,
    SphereGridParser.ContentHeaderSize);
SphereGridFile reparsedTypeEdit = SphereGridParser.Read(
    typeEdited.LayoutBytes,
    typeEdited.ContentBytes,
    SphereGridKind.Standard);
Assert(reparsedTypeEdit.Nodes[0].Type == 0x05, "Edited authoritative type");
Assert(reparsedTypeEdit.Nodes[0].RedundantType == 0x05, "Edited redundant type");

SphereGridNode[] positionEditedNodes = synthetic.Nodes.ToArray();
positionEditedNodes[2] = positionEditedNodes[2] with { X = -321, Y = 456 };
SphereGridWriteResult positionEdited = SphereGridWriter.Write(
    synthetic, synthetic.Clusters, positionEditedNodes, synthetic.Links);
AssertChangedOnly(
    layout,
    positionEdited.LayoutBytes,
    synthetic.Nodes[2].FileOffset,
    synthetic.Nodes[2].FileOffset + 1,
    synthetic.Nodes[2].FileOffset + 2,
    synthetic.Nodes[2].FileOffset + 3);
Assert(
    content.SequenceEqual(positionEdited.ContentBytes),
    "Position edit must not change node contents");

SphereGridLink[] linkEditedLinks = synthetic.Links.ToArray();
linkEditedLinks[0] = linkEditedLinks[0] with { AnchorNodeIndex = 2 };
SphereGridWriteResult linkEdited = SphereGridWriter.Write(
    synthetic, synthetic.Clusters, synthetic.Nodes, linkEditedLinks);
AssertChangedOnly(
    layout,
    linkEdited.LayoutBytes,
    synthetic.Links[0].FileOffset + 4,
    synthetic.Links[0].FileOffset + 5);
Assert(
    content.SequenceEqual(linkEdited.ContentBytes),
    "Link edit must not change node contents");
SphereGridFile reparsedLinkEdit = SphereGridParser.Read(
    linkEdited.LayoutBytes,
    linkEdited.ContentBytes,
    SphereGridKind.Standard);
Assert(reparsedLinkEdit.Links[0].AnchorNodeIndex == 2, "Edited link anchor");

var expandedNodes = synthetic.Nodes.ToList();
SphereGridNode connection = synthetic.Nodes[1];
expandedNodes.Add(new SphereGridNode(
    expandedNodes.Count,
    0,
    55,
    65,
    connection.Unknown04,
    0x02,
    connection.ClusterIndex,
    connection.Unknown0A,
    0x02));
var expandedLinks = synthetic.Links.ToList();
expandedLinks.Add(new SphereGridLink(
    expandedLinks.Count,
    0,
    1,
    3,
    ushort.MaxValue,
    0));
SphereGridWriteResult expanded = SphereGridWriter.Write(
    synthetic, synthetic.Clusters, expandedNodes, expandedLinks);
SphereGridFile reparsedExpanded = SphereGridParser.Read(
    expanded.LayoutBytes,
    expanded.ContentBytes,
    SphereGridKind.Standard);
Assert(reparsedExpanded.Nodes.Count == 4, "Expanded node count");
Assert(reparsedExpanded.Links.Count == 3, "Expanded link count");
Assert(
    BinaryPrimitives.ReadUInt16LittleEndian(expanded.ContentBytes.AsSpan(2, 2)) == 4,
    "Expanded content header node count");
Assert(reparsedExpanded.Nodes[3].X == 55, "Appended node position");
Assert(reparsedExpanded.Nodes[3].Type == 0x02, "Appended node content type");
Assert(reparsedExpanded.Links[2].NodeAIndex == 1, "Appended link first endpoint");
Assert(reparsedExpanded.Links[2].NodeBIndex == 3, "Appended link second endpoint");
Assert(reparsedExpanded.Links[2].AnchorNodeIndex == ushort.MaxValue, "Appended link is straight");
TestHeaderRepair(expanded);

SphereGridNode[] invalidClusterNodes = synthetic.Nodes.ToArray();
invalidClusterNodes[0] = invalidClusterNodes[0] with { ClusterIndex = 1 };
AssertThrows<InvalidDataException>(
    () => SphereGridWriter.Write(
        synthetic, synthetic.Clusters, invalidClusterNodes, synthetic.Links),
    "Writer must reject an invalid cluster");

SphereGridCluster[] invalidVisualClusters = synthetic.Clusters.ToArray();
invalidVisualClusters[0] = invalidVisualClusters[0] with { Type = 8 };
AssertThrows<InvalidDataException>(
    () => SphereGridWriter.Write(
        synthetic, invalidVisualClusters, synthetic.Nodes, synthetic.Links),
    "Writer must reject an invalid cluster visual type");

byte[] badReferenceLayout = (byte[])layout.Clone();
int nodeBase = SphereGridParser.HeaderSize + SphereGridParser.ClusterSize;
WriteUInt16(badReferenceLayout, nodeBase + 8, 1);
AssertThrows<InvalidDataException>(
    () => SphereGridParser.Read(
        badReferenceLayout, content, SphereGridKind.Standard),
    "Invalid cluster reference");

byte[] badLengthLayout = layout[..^1];
AssertThrows<InvalidDataException>(
    () => SphereGridParser.Read(
        badLengthLayout, content, SphereGridKind.Standard),
    "Invalid layout length");

if (args.Length == 1 && Directory.Exists(args[0]))
{
    var expectations = new Dictionary<SphereGridKind, (int Clusters, int Nodes, int Links)>
    {
        [SphereGridKind.Original] = (89, 828, 848),
        [SphereGridKind.Standard] = (98, 860, 881),
        [SphereGridKind.Expert] = (122, 828, 834)
    };
    foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
    {
        SphereGridFile grid = SphereGridParser.Read(
            SphereGridFileSet.FromDirectory(args[0], kind));
        SphereGridWriteResult roundTrip = SphereGridWriter.Write(grid);
        Assert(
            grid.OriginalLayoutBytes.SequenceEqual(roundTrip.LayoutBytes),
            $"{kind} layout round trip");
        Assert(
            grid.OriginalContentBytes.SequenceEqual(roundTrip.ContentBytes),
            $"{kind} content round trip");
        (int expectedClusters, int expectedNodes, int expectedLinks) = expectations[kind];
        Assert(grid.Clusters.Count == expectedClusters, $"{kind} cluster count");
        Assert(grid.Nodes.Count == expectedNodes, $"{kind} node count");
        Assert(grid.Links.Count == expectedLinks, $"{kind} link count");
        int expectedVisibleNodes = kind == SphereGridKind.Expert ? 805 : expectedNodes;
        var graph = new SphereGridGraph(grid);
        Assert(
            graph.VisibleNodes.Count == expectedVisibleNodes,
            $"{kind} visible node count");
        string routeCounts = string.Join(", ", graph.VisibleNodes
            .GroupBy(node => graph.Routes.GetCharacter(node.Index))
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}"));
        Console.WriteLine(
            $"{kind}: {grid.Clusters.Count} clusters, {grid.Nodes.Count} nodes, " +
            $"{grid.Links.Count} links, {grid.Nodes.Count(node => !node.ContentMatchesLayout)} " +
            $"content mismatches. Routes: {routeCounts}.");
    }
}
else
{
    Console.WriteLine("Synthetic sphere-grid smoke tests passed.");
}

TestSaveTransaction(synthetic);

static void TestSaveTransaction(SphereGridFile synthetic)
{
    string directory = Path.Combine(
        Path.GetTempPath(), "ZanarkandWorkshop-SphereGridSmoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string layoutPath = Path.Combine(directory, "dat02.dat");
    string contentPath = Path.Combine(directory, "dat10.dat");
    try
    {
        File.WriteAllBytes(layoutPath, synthetic.OriginalLayoutBytes);
        File.WriteAllBytes(contentPath, synthetic.OriginalContentBytes);
        SphereGridFile diskGrid = SphereGridParser.Read(
            new SphereGridFileSet(SphereGridKind.Standard, layoutPath, contentPath));

        SphereGridNode[] editedNodes = diskGrid.Nodes.ToArray();
        editedNodes[1] = editedNodes[1] with { Type = 0x28 };
        SphereGridWriteResult output = SphereGridWriter.Write(
            diskGrid, diskGrid.Clusters, editedNodes, diskGrid.Links);
        _ = SphereGridSaveTransaction.Save(diskGrid, output);
        Assert(
            File.ReadAllBytes(layoutPath).SequenceEqual(output.LayoutBytes),
            "Saved layout");
        Assert(
            File.ReadAllBytes(contentPath).SequenceEqual(output.ContentBytes),
            "Saved contents");
        Assert(
            !File.Exists(layoutPath + ".zwbak"),
            "Save must not create a persistent layout backup");
        Assert(
            !File.Exists(contentPath + ".zwbak"),
            "Save must not create a persistent content backup");

        // Restore a known pair, then force the second replacement to fail. The first
        // replacement must be rolled back before Save returns an error.
        File.WriteAllBytes(layoutPath, diskGrid.OriginalLayoutBytes);
        File.WriteAllBytes(contentPath, diskGrid.OriginalContentBytes);
        SphereGridFile rollbackSource = SphereGridParser.Read(
            new SphereGridFileSet(SphereGridKind.Standard, layoutPath, contentPath));
        SphereGridNode[] rollbackNodes = rollbackSource.Nodes.ToArray();
        rollbackNodes[2] = rollbackNodes[2] with { Type = 0x38 };
        SphereGridWriteResult rollbackOutput = SphereGridWriter.Write(
            rollbackSource, rollbackSource.Clusters, rollbackNodes, rollbackSource.Links);

        using (FileStream lockedContent = new(
            contentPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertThrows<IOException>(
                () => SphereGridSaveTransaction.Save(rollbackSource, rollbackOutput),
                "Locked second file must fail the paired save");
        }
        Assert(
            File.ReadAllBytes(layoutPath).SequenceEqual(rollbackSource.OriginalLayoutBytes),
            "Failed save must roll back layout");
        Assert(
            File.ReadAllBytes(contentPath).SequenceEqual(rollbackSource.OriginalContentBytes),
            "Failed save must preserve contents");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void TestHeaderRepair(SphereGridWriteResult expanded)
{
    string directory = Path.Combine(
        Path.GetTempPath(), "ZanarkandWorkshop-SphereGridRepair-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var files = new SphereGridFileSet(
        SphereGridKind.Standard,
        Path.Combine(directory, "dat02.dat"),
        Path.Combine(directory, "dat10.dat"));
    try
    {
        byte[] staleContent = expanded.ContentBytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(staleContent.AsSpan(2, 2), 3);
        File.WriteAllBytes(files.LayoutPath, expanded.LayoutBytes);
        File.WriteAllBytes(files.ContentPath, staleContent);

        SphereGridHeaderMismatch? mismatch = SphereGridHeaderRepair.Inspect(files);
        Assert(mismatch is not null, "Stale content count must be detected");
        Assert(mismatch!.CanRepair, "Complete stale content table must be repairable");
        Assert(mismatch.LayoutNodeCount == 4, "Repair layout count");
        Assert(mismatch.ContentNodeCount == 3, "Repair stale content count");
        SphereGridHeaderRepair.Repair(mismatch);

        Assert(SphereGridHeaderRepair.Inspect(files) is null, "Repair must synchronize counts");
        Assert(
            !File.Exists(files.ContentPath + ".zwbak"),
            "Repair must not create a persistent content backup");
        Assert(SphereGridParser.Read(files).Nodes.Count == 4, "Repaired pair must parse");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void AssertChangedOnly(byte[] before, byte[] after, params int[] allowedOffsets)
{
    Assert(before.Length == after.Length, "Mutation changed file length");
    HashSet<int> allowed = allowedOffsets.ToHashSet();
    var actual = new List<int>();
    for (int index = 0; index < before.Length; index++)
    {
        if (before[index] != after[index])
            actual.Add(index);
    }
    Assert(actual.Count > 0, "Expected mutation made no byte changes");
    Assert(
        actual.All(allowed.Contains),
        $"Mutation changed unexpected offsets: {string.Join(", ", actual.Where(x => !allowed.Contains(x)).Select(x => $"0x{x:X}"))}");
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new Exception(message);
}
