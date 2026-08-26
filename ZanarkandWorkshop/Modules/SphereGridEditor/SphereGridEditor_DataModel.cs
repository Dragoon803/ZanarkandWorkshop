using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.SphereGridEditor;

public partial class SphereGridEditor_DataModel : ObservableObject
{
    private readonly Dictionary<SphereGridKind, EditSession> _sessions = new();

    [ObservableProperty] private SphereGridKind selectedGrid;
    [ObservableProperty] private SphereGridGraph? graph;
    [ObservableProperty] private SphereGridNode? selectedNode;
    [ObservableProperty] private SphereGridNodeTypeInfo? pendingNodeType;
    [ObservableProperty] private SphereGridCharacter? pendingCharacter;
    [ObservableProperty] private decimal? pendingX;
    [ObservableProperty] private decimal? pendingY;
    [ObservableProperty] private IReadOnlyDictionary<int, SphereGridCharacter>
        colorOverrides = new Dictionary<int, SphereGridCharacter>();
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string historyStatus = "";
    [ObservableProperty] private SphereGridNodeTypeInfo? findNodeType;
    [ObservableProperty] private SphereGridNodeTypeInfo? replacementNodeType;
    [ObservableProperty] private string findReplaceStatus = "Choose a node type to find.";
    [ObservableProperty] private SphereGridNodeTypeInfo? newNodeType;
    [ObservableProperty] private SphereGridCharacter newNodeCharacter;
    [ObservableProperty] private decimal newNodeX;
    [ObservableProperty] private decimal newNodeY;
    [ObservableProperty] private string experimentalStatus =
        "Select an existing node whose section settings the new node should reuse.";
    [ObservableProperty] private decimal selectedExperimentalLinkIndex;
    [ObservableProperty] private decimal? pendingLinkNodeA;
    [ObservableProperty] private decimal? pendingLinkNodeB;
    [ObservableProperty] private decimal? pendingLinkAnchor;
    [ObservableProperty] private bool canCommitSelectedLink;
    [ObservableProperty] private bool experimentalLinkHighlightEnabled;
    [ObservableProperty] private bool hasSelectedExperimentalLink;
    private bool _updatingLinkControls;
    private bool _updatingNodeControls;
    private bool _updatingCoordinateControls;
    private EditSnapshot? _dragStartSnapshot;
    private int _dragNodeIndex = -1;
    private int _lastFindIndex = -1;

    public IReadOnlyList<SphereGridNodeTypeInfo> NodeTypeOptions =>
        SphereGridNodeTypes.All;

    public IReadOnlyList<SphereGridCharacter> CharacterOptions { get; } =
        Enum.GetValues<SphereGridCharacter>()
            .Where(character => character != SphereGridCharacter.Unassigned)
            .ToArray();

    public bool HasSelectedNode => SelectedNode is not null;
    public string CreateNodeInstruction => Graph is not null &&
        Graph.File.Nodes.Count >= SphereGridValidator.MaximumGameCompatibleNodes
            ? $"This grid has reached the safe in-game limit of {SphereGridValidator.MaximumGameCompatibleNodes} nodes."
            : SelectedNode is null
                ? "Select a nearby node on the grid before creating a new node."
                : $"Creates an unconnected node using Node #{SelectedNode.Index}'s section settings.";
    public string SelectedLinkNumber => HasSelectedExperimentalLink
        ? decimal.ToInt32(decimal.Round(SelectedExperimentalLinkIndex)).ToString()
        : "";
    public int HighlightedExperimentalLinkIndex =>
        ExperimentalLinkHighlightEnabled && HasSelectedExperimentalLink
        ? decimal.ToInt32(decimal.Round(SelectedExperimentalLinkIndex))
        : -1;
    public int ExperimentalPreviewAnchorIndex =>
        PendingLinkAnchor is decimal anchor
            ? decimal.ToInt32(decimal.Round(anchor))
            : -1;
    public bool CanReplaceNodeTypes =>
        FindNodeType is not null && ReplacementNodeType is not null &&
        FindNodeType.Id != ReplacementNodeType.Id;
    public bool CanReplaceSelectedNode => HasSelectedNode && CanReplaceNodeTypes;
    public bool CanAddExperimentalNode =>
        SelectedNode is not null && Graph is not null &&
        Graph.File.Nodes.Count < SphereGridValidator.MaximumGameCompatibleNodes;
    public bool CanAddExperimentalLink
    {
        get
        {
            if (Graph is null ||
                Graph.File.Links.Count >= CurrentGameCompatibleLinkLimit)
                return false;
            if (PendingLinkNodeA is not decimal pendingA ||
                PendingLinkNodeB is not decimal pendingB ||
                PendingLinkAnchor is not decimal pendingAnchor)
                return false;
            int a = decimal.ToInt32(decimal.Round(pendingA));
            int b = decimal.ToInt32(decimal.Round(pendingB));
            int anchor = decimal.ToInt32(decimal.Round(pendingAnchor));
            if (a < 0 || a >= Graph.File.Nodes.Count ||
                b < 0 || b >= Graph.File.Nodes.Count || a == b ||
                (anchor != ushort.MaxValue &&
                 (anchor < 0 || anchor >= Graph.File.Nodes.Count)))
                return false;
            return !Graph.File.Links.Any(link =>
                (link.NodeAIndex == a && link.NodeBIndex == b) ||
                (link.NodeAIndex == b && link.NodeBIndex == a)) &&
                HasUsableEndpointCapacity(a, b);
        }
    }
    public int CurrentGameCompatibleLinkLimit =>
        SphereGridValidator.GetGameCompatibleLinkLimit(SelectedGrid);
    public bool CanCreateExperimentalLink => Graph is not null &&
        Graph.File.Links.Count < CurrentGameCompatibleLinkLimit;
    public string NodeCapacityText => Graph is null ? "Nodes: 0 / 860" :
        $"Nodes: {Graph.File.Nodes.Count} / {SphereGridValidator.MaximumGameCompatibleNodes:N0}";
    public string LinkCapacityText => Graph is null ? "Links: 0" :
        $"Links: {Graph.File.Links.Count} / {CurrentGameCompatibleLinkLimit}";
    public string LinkPointBudgetText
    {
        get
        {
            if (Graph is null)
                return "Link Points: 0 / 4,096";
            SphereGridLinkPointBudget budget =
                SphereGridLinkPointBudget.Calculate(Graph.File.Links);
            return budget.MinimumPoints == budget.MaximumPoints
                ? $"Link Points: {budget.MinimumPoints:N0} / {SphereGridLinkPointBudget.Capacity:N0}"
                : $"Link Points: {budget.MinimumPoints:N0}-{budget.MaximumPoints:N0} / " +
                  $"{SphereGridLinkPointBudget.Capacity:N0} (diagnostic)";
        }
    }
    public string SelectedNodeConnectionText
    {
        get
        {
            if (Graph is null || SelectedNode is null)
                return $"Selected Node Connections: - / {SphereGridValidator.MaximumUsableLinksPerNode}";
            int count = Graph.File.Links.Count(link =>
                link.NodeAIndex == SelectedNode.Index || link.NodeBIndex == SelectedNode.Index);
            return $"Selected Node Connections: {count} / {SphereGridValidator.MaximumUsableLinksPerNode}";
        }
    }

    public bool TryValidateNewLink(
        int nodeA, int nodeB, int anchor, out string message)
    {
        if (Graph is null)
        {
            message = "No sphere grid is loaded.";
            return false;
        }
        if (Graph.File.Links.Count >= CurrentGameCompatibleLinkLimit)
        {
            message = $"This {SelectedGrid} grid has reached its tested " +
                      $"{CurrentGameCompatibleLinkLimit}-link limit.";
            return false;
        }
        if (nodeA < 0 || nodeA >= Graph.File.Nodes.Count ||
            nodeB < 0 || nodeB >= Graph.File.Nodes.Count)
        {
            message = $"Choose node numbers between 0 and {Graph.File.Nodes.Count - 1}.";
            return false;
        }
        if (nodeA == nodeB)
        {
            message = "Node A and Node B must be different nodes.";
            return false;
        }
        if (!TryValidateNewLinkEndpoint(nodeA, out message) ||
            !TryValidateNewLinkEndpoint(nodeB, out message))
            return false;
        if (anchor != ushort.MaxValue && (anchor < 0 || anchor >= Graph.File.Nodes.Count))
        {
            message = $"Use a node number between 0 and {Graph.File.Nodes.Count - 1}, or 65535 for a straight link.";
            return false;
        }
        if (Graph.File.Links.Any(link =>
            (link.NodeAIndex == nodeA && link.NodeBIndex == nodeB) ||
            (link.NodeAIndex == nodeB && link.NodeBIndex == nodeA)))
        {
            message = "A link already connects these two nodes.";
            return false;
        }
        if (!HasUsableEndpointCapacity(nodeA, nodeB))
        {
            message = $"FFX only allows movement or activation through " +
                      $"{SphereGridValidator.MaximumUsableLinksPerNode} links on one node.";
            return false;
        }
        message = "";
        return true;
    }

    public bool TryValidateNewLinkEndpoint(int nodeIndex, out string message)
    {
        if (Graph is null || nodeIndex < 0 || nodeIndex >= Graph.File.Nodes.Count)
        {
            message = "That node is not available in the current Sphere Grid.";
            return false;
        }

        int connectionCount = Graph.File.Links.Count(link =>
            link.NodeAIndex == nodeIndex || link.NodeBIndex == nodeIndex);
        if (connectionCount >= SphereGridValidator.MaximumUsableLinksPerNode)
        {
            message = $"Node #{nodeIndex} already has the maximum " +
                      $"{SphereGridValidator.MaximumUsableLinksPerNode} links.";
            return false;
        }

        message = "";
        return true;
    }

    private bool HasUsableEndpointCapacity(int nodeA, int nodeB, int excludedLinkIndex = -1)
    {
        if (Graph is null)
            return false;

        int countA = 0;
        int countB = 0;
        foreach (SphereGridLink link in Graph.File.Links)
        {
            if (link.Index == excludedLinkIndex)
                continue;
            if (link.NodeAIndex == nodeA || link.NodeBIndex == nodeA)
                countA++;
            if (link.NodeAIndex == nodeB || link.NodeBIndex == nodeB)
                countB++;
        }

        return countA < SphereGridValidator.MaximumUsableLinksPerNode &&
               countB < SphereGridValidator.MaximumUsableLinksPerNode;
    }
    public string ExperimentalConnectionSummary => SelectedNode is null
        ? "No connection node selected"
        : $"Node #{SelectedNode.Index}  ·  Cluster {SelectedNode.ClusterIndex}";
    public bool CanUndo =>
        _sessions.TryGetValue(SelectedGrid, out EditSession? session) &&
        session.UndoHistory.Count > 0;
    public bool CanRedo =>
        _sessions.TryGetValue(SelectedGrid, out EditSession? session) &&
        session.RedoHistory.Count > 0;
    public bool CanUndoAll => IsDirty;
    public bool IsGridDirty(SphereGridKind kind) =>
        _sessions.TryGetValue(kind, out EditSession? session) &&
        (session.DirtyNodes.Count > 0 || session.DirtyLinks.Count > 0 ||
         session.Graph.File.Nodes.Count != session.SourceFile.Nodes.Count ||
         session.Graph.File.Links.Count != session.SourceFile.Links.Count);
    public bool HasAnyPendingChanges =>
        IsDirty || _sessions.Keys.Any(IsGridDirty);
    public bool HasExperimentalStructureChanges =>
        _sessions.TryGetValue(SelectedGrid, out EditSession? session) &&
        (session.Graph.File.Nodes.Count != session.SourceFile.Nodes.Count ||
         session.Graph.File.Links.Count != session.SourceFile.Links.Count);

    public string CurrentLayoutPath =>
        _sessions.TryGetValue(SelectedGrid, out EditSession? session)
            ? session.SourceFile.LayoutPath
            : "";

    public string CurrentContentPath =>
        _sessions.TryGetValue(SelectedGrid, out EditSession? session)
            ? session.SourceFile.ContentPath
            : "";

    public string SelectedNodeSummary => SelectedNode is null
        ? "Select a node to inspect or edit it."
        : $"Node #{SelectedNode.Index}  ·  {SelectedNode.TypeInfo.Name}  ·  " +
          $"{GetEffectiveCharacter(SelectedNode.Index)} section  ·  " +
          $"Position {SelectedNode.X}, {SelectedNode.Y}  ·  " +
          $"Cluster {SelectedNode.ClusterIndex}";

    public SphereGridEditor_DataModel() => Load(SphereGridKind.Standard);

    public void RestoreOriginalAndReload(string originalDirectory)
    {
        SphereGridKind selectedKind = SelectedGrid;
        foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
        {
            SphereGridFileSet originalFiles = SphereGridFileSet.FromDirectory(originalDirectory, kind);
            _ = SphereGridParser.Read(originalFiles);
        }

        Directory.CreateDirectory(Project_Service.Instance.Path_SphereGrid);
        foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
        {
            SphereGridFileSet originalFiles = SphereGridFileSet.FromDirectory(originalDirectory, kind);
            SphereGridFileSet projectFiles = SphereGridFileSet.FromDirectory(
                Project_Service.Instance.Path_SphereGrid, kind);
            File.Copy(originalFiles.LayoutPath, projectFiles.LayoutPath, true);
            File.Copy(originalFiles.ContentPath, projectFiles.ContentPath, true);
        }

        SphereGridColorMetadata.Delete(
            Project_Service.Instance.Path_ZanarkandWorkshopMetadata);
        SphereGridColorMetadata.Delete(
            Project_Service.Instance.Path_LegacyProjectMetadata);
        SphereGridColorMetadata.Delete(Project_Service.Instance.Path_SphereGrid);
        _sessions.Clear();
        Load(selectedKind);
        Status = "Restored and reloaded the Original, Standard, and Expert Sphere Grids.";
    }

    public void Load(SphereGridKind kind)
    {
        if (Project_Service.Instance.IsProjectRegistered)
            SphereGridColorMetadata.MigrateLegacyLocation(
                Project_Service.Instance.Path_ZanarkandWorkshopMetadata,
                Project_Service.Instance.Path_PathHashedProjectMetadata);
        SphereGridColorMetadata.MigrateLegacyLocation(
            Project_Service.Instance.Path_ZanarkandWorkshopMetadata,
            Project_Service.Instance.Path_LegacyProjectMetadata);
        SphereGridColorMetadata.MigrateLegacyLocation(
            Project_Service.Instance.Path_ZanarkandWorkshopMetadata,
            Project_Service.Instance.Path_SphereGrid);
        if (!_sessions.TryGetValue(kind, out EditSession? session))
        {
            SphereGridFileSet files = SphereGridFileSet.FromDirectory(
                Project_Service.Instance.Path_SphereGrid, kind);
            SphereGridFile file = SphereGridParser.Read(files);
            var graph = new SphereGridGraph(file);
            session = new EditSession(file, graph);
            foreach ((int nodeIndex, SphereGridCharacter character) in
                     SphereGridColorMetadata.Load(
                         Project_Service.Instance.Path_ZanarkandWorkshopMetadata, kind))
            {
                if ((uint)nodeIndex < (uint)file.Nodes.Count &&
                    file.Nodes[nodeIndex].IsVisible &&
                    character != SphereGridCharacter.Unassigned &&
                    character != graph.Routes.GetCharacter(nodeIndex))
                {
                    session.ColorOverrides[nodeIndex] = character;
                    session.SavedColorOverrides[nodeIndex] = character;
                }
            }
            _sessions.Add(kind, session);
        }

        Graph = session.Graph;
        ColorOverrides =
            new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        IsDirty = session.DirtyNodes.Count > 0 || session.DirtyLinks.Count > 0;
        SelectedGrid = kind;
        SelectedNode = null;
        ClearLinkSelection();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        _lastFindIndex = -1;
        RefreshFindReplaceStatus();
        HistoryStatus = "";
        UpdateStatus();
    }

    public SphereGridNode? FindNextNode()
    {
        if (Graph is null || FindNodeType is null)
            return null;
        SphereGridNode[] matches = Graph.VisibleNodes
            .Where(node => node.Type == FindNodeType.Id)
            .OrderBy(node => node.Index)
            .ToArray();
        if (matches.Length == 0)
        {
            FindReplaceStatus = $"No matches found for {FindNodeType.Name}.";
            return null;
        }

        SphereGridNode match = matches.FirstOrDefault(node => node.Index > _lastFindIndex)
            ?? matches[0];
        _lastFindIndex = match.Index;
        SelectedNode = Graph.File.Nodes[match.Index];
        int position = Array.FindIndex(matches, node => node.Index == match.Index) + 1;
        FindReplaceStatus = $"Match {position} of {matches.Length}  ·  Node #{match.Index}  ·  {FindNodeType.Name}";
        return SelectedNode;
    }

    public SphereGridNode? AddExperimentalNode()
    {
        if (!CanAddExperimentalNode || Graph is null || SelectedNode is null ||
            NewNodeType is null || NewNodeCharacter == SphereGridCharacter.Unassigned)
            return null;

        EditSession session = _sessions[SelectedGrid];
        RecordUndoCheckpoint(session, description: $"created node #{session.Graph.File.Nodes.Count} ({NewNodeType.Name})");
        SphereGridNode connection = session.Graph.File.Nodes[SelectedNode.Index];
        var nodes = session.Graph.File.Nodes.ToList();
        int nodeIndex = nodes.Count;
        short x = (short)Math.Clamp(
            decimal.ToInt32(decimal.Round(NewNodeX)), short.MinValue, short.MaxValue);
        short y = (short)Math.Clamp(
            decimal.ToInt32(decimal.Round(NewNodeY)), short.MinValue, short.MaxValue);
        int nodeOffset = SphereGridParser.HeaderSize +
            session.Graph.File.Clusters.Count * SphereGridParser.ClusterSize +
            nodeIndex * SphereGridParser.NodeSize;
        nodes.Add(new SphereGridNode(
            nodeIndex,
            nodeOffset,
            x,
            y,
            connection.Unknown04,
            NewNodeType.Id,
            connection.ClusterIndex,
            connection.Unknown0A,
            NewNodeType.Id));

        SphereGridFile expanded = CopyWithGraphData(
            session.Graph.File,
            session.Graph.File.Clusters,
            nodes,
            session.Graph.File.Links);
        session.Graph = new SphereGridGraph(expanded, session.Graph.Routes);
        session.ColorOverrides[nodeIndex] = NewNodeCharacter;
        session.DirtyNodes.Add(nodeIndex);
        Graph = session.Graph;
        ColorOverrides = new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        SelectedNode = session.Graph.File.Nodes[nodeIndex];
        IsDirty = true;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        OnPropertyChanged(nameof(CanAddExperimentalLink));
        OnPropertyChanged(nameof(CanCreateExperimentalLink));
        ExperimentalStatus =
            $"Added unconnected node #{nodeIndex} using Cluster {connection.ClusterIndex}.";
        UpdateStatus(ExperimentalStatus);
        return SelectedNode;
    }

    public SphereGridLink? AddExperimentalLink()
    {
        if (!CanAddExperimentalLink || Graph is null)
            return null;
        EditSession session = _sessions[SelectedGrid];
        RecordUndoCheckpoint(session, description: $"created link #{session.Graph.File.Links.Count}");
        var links = session.Graph.File.Links.ToList();
        int linkIndex = links.Count;
        int linkBase = SphereGridParser.HeaderSize +
            session.Graph.File.Clusters.Count * SphereGridParser.ClusterSize +
            session.Graph.File.Nodes.Count * SphereGridParser.NodeSize;
        links.Add(new SphereGridLink(
            linkIndex,
            linkBase + linkIndex * SphereGridParser.LinkSize,
            checked((ushort)decimal.ToInt32(decimal.Round(PendingLinkNodeA!.Value))),
            checked((ushort)decimal.ToInt32(decimal.Round(PendingLinkNodeB!.Value))),
            checked((ushort)decimal.ToInt32(decimal.Round(PendingLinkAnchor!.Value))),
            0));
        SphereGridFile expanded = CopyWithGraphData(
            session.Graph.File,
            session.Graph.File.Clusters,
            session.Graph.File.Nodes,
            links);
        session.Graph = new SphereGridGraph(expanded, session.Graph.Routes);
        session.DirtyLinks.Add(linkIndex);
        Graph = session.Graph;
        IsDirty = true;
        SelectExperimentalLink(linkIndex);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        OnPropertyChanged(nameof(CanAddExperimentalLink));
        OnPropertyChanged(nameof(CanCreateExperimentalLink));
        ExperimentalStatus = $"Added link #{linkIndex}.";
        UpdateStatus(ExperimentalStatus);
        return links[^1];
    }

    public SphereGridLink? AddExperimentalLink(int nodeA, int nodeB, int anchor)
    {
        if (!TryValidateNewLink(nodeA, nodeB, anchor, out _))
            return null;
        _updatingLinkControls = true;
        PendingLinkNodeA = nodeA;
        PendingLinkNodeB = nodeB;
        PendingLinkAnchor = anchor;
        _updatingLinkControls = false;
        return AddExperimentalLink();
    }

    private void CommitSelectedLinkEdit()
    {
        if (!CanCommitSelectedLink || Graph is null)
            return;
        EditSession session = _sessions[SelectedGrid];
        int linkIndex = decimal.ToInt32(decimal.Round(SelectedExperimentalLinkIndex));
        ushort nodeA = checked((ushort)decimal.ToInt32(decimal.Round(PendingLinkNodeA!.Value)));
        ushort nodeB = checked((ushort)decimal.ToInt32(decimal.Round(PendingLinkNodeB!.Value)));
        ushort anchor = checked((ushort)decimal.ToInt32(decimal.Round(PendingLinkAnchor!.Value)));

        SphereGridLink[] links = session.Graph.File.Links.ToArray();
        links[linkIndex] = links[linkIndex] with
        {
            NodeAIndex = nodeA,
            NodeBIndex = nodeB,
            AnchorNodeIndex = anchor
        };
        SphereGridFile edited = CopyWithGraphData(
            session.Graph.File, session.Graph.File.Clusters,
            session.Graph.File.Nodes, links);
        SphereGridValidator.ValidateReferences(edited);
        RecordUndoCheckpoint(session, description: $"edited link #{linkIndex}: {nodeA} ↔ {nodeB}, anchor {anchor}");
        session.Graph = new SphereGridGraph(edited, session.Graph.Routes);
        session.DirtyLinks.Add(linkIndex);
        Graph = session.Graph;
        IsDirty = true;
        RefreshExperimentalLinkState();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanUndoAll));
        ExperimentalStatus = $"Updated link #{linkIndex}: {nodeA} ↔ {nodeB}, anchor {anchor}.";
        UpdateStatus(ExperimentalStatus);
    }

    public bool ReplaceSelectedNodeType()
    {
        if (SelectedNode is null || ReplacementNodeType is null)
            return false;
        PendingNodeType = ReplacementNodeType;
        RefreshFindReplaceStatus();
        return true;
    }

    public int ReplaceAllNodeTypes()
    {
        if (Graph is null || FindNodeType is null || ReplacementNodeType is null ||
            FindNodeType.Id == ReplacementNodeType.Id)
            return 0;
        EditSession session = _sessions[SelectedGrid];
        SphereGridNode[] nodes = session.Graph.File.Nodes.ToArray();
        int[] matches = session.Graph.VisibleNodes
            .Where(node => node.Type == FindNodeType.Id)
            .Select(node => node.Index)
            .ToArray();
        if (matches.Length == 0)
        {
            RefreshFindReplaceStatus();
            return 0;
        }

        RecordUndoCheckpoint(session, description:
            $"Replace All: {matches.Length} {FindNodeType.Name} node{(matches.Length == 1 ? "" : "s")} → {ReplacementNodeType.Name}");
        foreach (int nodeIndex in matches)
        {
            nodes[nodeIndex] = nodes[nodeIndex] with { Type = ReplacementNodeType.Id };
            session.DirtyNodes.Add(nodeIndex);
        }
        int? selectedIndex = SelectedNode?.Index;
        session.Graph = new SphereGridGraph(
            CopyWithNodes(session.Graph.File, nodes), session.Graph.Routes);
        Graph = session.Graph;
        SelectedNode = selectedIndex is int index ? session.Graph.File.Nodes[index] : null;
        IsDirty = true;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanUndoAll));
        _lastFindIndex = -1;
        string matchWord = matches.Length == 1 ? "match" : "matches";
        FindReplaceStatus = $"Replaced {matches.Length} {matchWord}: {FindNodeType.Name} → {ReplacementNodeType.Name}.";
        UpdateStatus(FindReplaceStatus);
        return matches.Length;
    }

    private void CommitSelectedNodeControls()
    {
        if (_updatingNodeControls || _dragStartSnapshot is not null)
            return;
        if (SelectedNode is null || Graph is null || PendingNodeType is null ||
            PendingCharacter is null || PendingX is null || PendingY is null)
            return;

        EditSession session = _sessions[SelectedGrid];
        int nodeIndex = SelectedNode.Index;
        SphereGridCharacter defaultCharacter =
            session.Graph.Routes.GetCharacter(nodeIndex);
        bool hadOverride = session.ColorOverrides.TryGetValue(
            nodeIndex, out SphereGridCharacter previousOverride);
        bool wantsOverride = PendingCharacter.Value != defaultCharacter;
        bool colorChanged = hadOverride != wantsOverride ||
            (wantsOverride && previousOverride != PendingCharacter.Value);
        SphereGridNode current = session.Graph.File.Nodes[nodeIndex];
        short targetX = (short)Math.Clamp(
            decimal.ToInt32(decimal.Round(PendingX.Value)), short.MinValue, short.MaxValue);
        short targetY = (short)Math.Clamp(
            decimal.ToInt32(decimal.Round(PendingY.Value)), short.MinValue, short.MaxValue);
        bool nodeChanged = current.Type != PendingNodeType.Id ||
                           current.X != targetX || current.Y != targetY;
        if (!colorChanged && !nodeChanged)
            return;
        string nodeDetails = nodeChanged
            ? $"node #{nodeIndex}: {current.TypeInfo.Name} at ({current.X}, {current.Y}) → {PendingNodeType.Name} at ({targetX}, {targetY})"
            : $"node #{nodeIndex} section color: {GetEffectiveCharacter(nodeIndex)} → {PendingCharacter.Value}";
        RecordUndoCheckpoint(session, description: nodeDetails);

        if (PendingCharacter.Value == defaultCharacter)
            session.ColorOverrides.Remove(nodeIndex);
        else
            session.ColorOverrides[nodeIndex] = PendingCharacter.Value;

        if (nodeChanged)
        {
            SphereGridNode[] nodes = session.Graph.File.Nodes.ToArray();
            nodes[nodeIndex] = current with
            {
                Type = PendingNodeType.Id,
                X = targetX,
                Y = targetY
            };
            SphereGridFile editedFile = CopyWithNodes(session.Graph.File, nodes);
            session.Graph = new SphereGridGraph(editedFile, session.Graph.Routes);
        }

        session.DirtyNodes.Add(nodeIndex);
        Graph = session.Graph;
        ColorOverrides =
            new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        SelectedNode = session.Graph.File.Nodes[nodeIndex];
        IsDirty = true;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanUndoAll));
        UpdateStatus($"Updated node #{nodeIndex} in memory");
    }

    public void RevertAll()
    {
        EditSession session = _sessions[SelectedGrid];
        int count = session.UndoHistory.Count;
        while (session.UndoHistory.Count > 0)
        {
            EditSnapshot snapshot = session.UndoHistory.Pop();
            session.RedoHistory.Push(CaptureSnapshot(session) with { Description = snapshot.Description });
            RestoreSnapshot(session, snapshot);
        }
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        HistoryStatus = $"Undid all: {count} Sphere Grid change{(count == 1 ? "" : "s")} since the last save.";
    }

    public void DiscardCurrentGridChanges()
    {
        EditSession session = _sessions[SelectedGrid];
        session.Graph = new SphereGridGraph(session.SourceFile, session.DefaultRoutes);
        session.ColorOverrides.Clear();
        foreach ((int index, SphereGridCharacter character) in session.SavedColorOverrides)
            session.ColorOverrides[index] = character;
        session.DirtyNodes.Clear();
        session.DirtyLinks.Clear();
        session.UndoHistory.Clear();
        session.RedoHistory.Clear();

        Graph = session.Graph;
        ColorOverrides = new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        SelectedNode = null;
        ClearLinkSelection();
        IsDirty = false;
        HistoryStatus = "";
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        _lastFindIndex = -1;
        RefreshFindReplaceStatus();
        UpdateStatus("Discarded unsaved Sphere Grid changes");
    }

    public void UndoLastChange()
    {
        EditSession session = _sessions[SelectedGrid];
        if (session.UndoHistory.Count == 0)
            return;
        EditSnapshot snapshot = session.UndoHistory.Pop();
        session.RedoHistory.Push(CaptureSnapshot(session) with { Description = snapshot.Description });
        RestoreSnapshot(session, snapshot);
        HistoryStatus = $"Undid: {snapshot.Description}.";
    }

    public void RedoLastChange()
    {
        EditSession session = _sessions[SelectedGrid];
        if (session.RedoHistory.Count == 0)
            return;
        EditSnapshot snapshot = session.RedoHistory.Pop();
        session.UndoHistory.Push(CaptureSnapshot(session) with { Description = snapshot.Description });
        RestoreSnapshot(session, snapshot);
        HistoryStatus = $"Redid: {snapshot.Description}.";
    }

    private void RestoreSnapshot(EditSession session, EditSnapshot snapshot)
    {
        session.Graph = snapshot.Graph;
        session.ColorOverrides.Clear();
        foreach ((int index, SphereGridCharacter character) in snapshot.ColorOverrides)
            session.ColorOverrides[index] = character;
        session.DirtyNodes.Clear();
        foreach (int index in snapshot.DirtyNodes)
            session.DirtyNodes.Add(index);
        session.DirtyLinks.Clear();
        foreach (int index in snapshot.DirtyLinks)
            session.DirtyLinks.Add(index);
        Graph = session.Graph;
        ColorOverrides = new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        SelectedNode = snapshot.SelectedNodeIndex is int selectedIndex
            ? session.Graph.File.Nodes[selectedIndex]
            : null;
        IsDirty = session.DirtyNodes.Count > 0 || session.DirtyLinks.Count > 0;
        if (snapshot.HasSelectedLink &&
            snapshot.SelectedLinkIndex >= 0 &&
            snapshot.SelectedLinkIndex < session.Graph.File.Links.Count)
        {
            SelectExperimentalLink(snapshot.SelectedLinkIndex);
        }
        else
        {
            ClearLinkSelection();
        }
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        _lastFindIndex = -1;
        RefreshFindReplaceStatus();
    }

    public void SaveCurrentGrid()
    {
        if (_dragStartSnapshot is not null)
            throw new InvalidOperationException(
                "Finish dragging the selected node before saving the grid.");

        EditSession session = _sessions[SelectedGrid];
        SphereGridValidator.ValidateGameCompatibleNodeCount(
            session.Graph.File.Nodes.Count);
        SphereGridValidator.ValidateGameCompatibleLinkCount(
            SelectedGrid, session.Graph.File.Links.Count);
        SphereGridValidator.ValidateGameCompatibleLinkDegree(session.Graph.File);
        SphereGridWriteResult output = SphereGridWriter.Write(
            session.SourceFile,
            session.Graph.File.Clusters,
            session.Graph.File.Nodes,
            session.Graph.File.Links);

        // Save validates and stages both files, installs them as one transaction,
        // and restores their pre-save contents in memory if either replacement fails.
        SphereGridFile savedFile = SphereGridSaveTransaction.Save(
            session.SourceFile, output);

        // Parse the bytes installed on disk once more so the editing baseline is
        // exactly what a subsequent project/game load will see.
        SphereGridFile reloaded = SphereGridParser.Read(new SphereGridFileSet(
            SelectedGrid,
            savedFile.LayoutPath,
            savedFile.ContentPath));

        SphereGridColorMetadata.Save(
            Project_Service.Instance.Path_ZanarkandWorkshopMetadata,
            SelectedGrid,
            session.ColorOverrides);

        int? selectedIndex = SelectedNode?.Index;
        SphereGridRouteMetadata routes = session.Graph.Routes;
        session.SourceFile = reloaded;
        session.Graph = new SphereGridGraph(reloaded, routes);
        session.DirtyNodes.Clear();
        session.DirtyLinks.Clear();
        session.UndoHistory.Clear();
        session.RedoHistory.Clear();
        session.SavedColorOverrides.Clear();
        foreach ((int colorIndex, SphereGridCharacter character) in session.ColorOverrides)
            session.SavedColorOverrides[colorIndex] = character;
        Graph = session.Graph;
        ColorOverrides =
            new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        SelectedNode = selectedIndex is int index
            ? session.Graph.File.Nodes[index]
            : null;
        IsDirty = false;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(HasExperimentalStructureChanges));
        HistoryStatus = "";
        UpdateStatus(EditorSaveStatus.Success("Sphere Grid"));
    }

    public void SaveToMaster(string masterPath, string metadataPath)
    {
        if (_dragStartSnapshot is not null)
            throw new InvalidOperationException(
                "Finish dragging the selected node before using Save As.");
        EditSession session = _sessions[SelectedGrid];
        SphereGridValidator.ValidateGameCompatibleNodeCount(session.Graph.File.Nodes.Count);
        SphereGridValidator.ValidateGameCompatibleLinkCount(SelectedGrid, session.Graph.File.Links.Count);
        SphereGridValidator.ValidateGameCompatibleLinkDegree(session.Graph.File);
        SphereGridWriteResult output = SphereGridWriter.Write(
            session.SourceFile, session.Graph.File.Clusters, session.Graph.File.Nodes, session.Graph.File.Links);
        SphereGridFileSet targetFiles = SphereGridFileSet.FromDirectory(
            Path.Combine(masterPath, "jppc", "menu", "abmap"), SelectedGrid);
        SphereGridFile targetSource = SphereGridParser.Read(targetFiles);
        _ = SphereGridSaveTransaction.Save(targetSource, output);
        SphereGridColorMetadata.Save(metadataPath, SelectedGrid, session.ColorOverrides);
    }

    public void ResetSelectedColor()
    {
        if (SelectedNode is null)
            return;
        EditSession session = _sessions[SelectedGrid];
        int nodeIndex = SelectedNode.Index;
        if (!session.ColorOverrides.TryGetValue(nodeIndex, out SphereGridCharacter previous))
            return;
        RecordUndoCheckpoint(session,
            description: $"node #{nodeIndex} color: {previous} → default");
        session.ColorOverrides.Remove(nodeIndex);
        session.DirtyNodes.Add(nodeIndex);
        ColorOverrides =
            new Dictionary<int, SphereGridCharacter>(session.ColorOverrides);
        PendingCharacter = session.Graph.Routes.GetCharacter(SelectedNode.Index);
        IsDirty = true;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanUndoAll));
        OnPropertyChanged(nameof(SelectedNodeSummary));
        UpdateStatus($"Reset node #{SelectedNode.Index} to its default color");
    }

    public SphereGridCharacter GetEffectiveCharacter(int nodeIndex)
    {
        if (ColorOverrides.TryGetValue(nodeIndex, out SphereGridCharacter character))
            return character;
        return Graph?.Routes.GetCharacter(nodeIndex) ??
               SphereGridCharacter.Unassigned;
    }

    partial void OnSelectedNodeChanged(SphereGridNode? value)
    {
        OnPropertyChanged(nameof(SelectedNodeConnectionText));
        if (value is not null)
        {
            _updatingNodeControls = true;
            PendingNodeType = value.TypeInfo;
            PendingCharacter = GetEffectiveCharacter(value.Index);
            _updatingCoordinateControls = true;
            PendingX = value.X;
            PendingY = value.Y;
            _updatingCoordinateControls = false;
            _updatingNodeControls = false;

        }
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(CreateNodeInstruction));
        OnPropertyChanged(nameof(CanReplaceSelectedNode));
        OnPropertyChanged(nameof(CanAddExperimentalNode));
        OnPropertyChanged(nameof(ExperimentalConnectionSummary));
        OnPropertyChanged(nameof(SelectedNodeSummary));
        if (value is not null)
        {
            NewNodeCharacter = GetEffectiveCharacter(value.Index);
            NewNodeX = value.X + 40;
            NewNodeY = value.Y;
            ExperimentalStatus =
                $"Ready to append an unconnected node using Node #{value.Index}'s section settings.";
        }
        else
        {
            _updatingNodeControls = true;
            PendingNodeType = null;
            PendingCharacter = null;
            _updatingCoordinateControls = true;
            PendingX = null;
            PendingY = null;
            _updatingCoordinateControls = false;
            _updatingNodeControls = false;
        }
    }

    partial void OnColorOverridesChanged(
        IReadOnlyDictionary<int, SphereGridCharacter> value) =>
        OnPropertyChanged(nameof(SelectedNodeSummary));

    partial void OnSelectedGridChanged(SphereGridKind value)
    {
        OnPropertyChanged(nameof(CurrentGameCompatibleLinkLimit));
        OnPropertyChanged(nameof(LinkCapacityText));
        OnPropertyChanged(nameof(CanAddExperimentalLink));
        OnPropertyChanged(nameof(CanCreateExperimentalLink));
    }

    partial void OnGraphChanged(SphereGridGraph? value)
    {
        OnPropertyChanged(nameof(CreateNodeInstruction));
        OnPropertyChanged(nameof(CanAddExperimentalNode));
        OnPropertyChanged(nameof(NodeCapacityText));
        OnPropertyChanged(nameof(LinkCapacityText));
        OnPropertyChanged(nameof(LinkPointBudgetText));
        OnPropertyChanged(nameof(SelectedNodeConnectionText));
    }

    partial void OnFindNodeTypeChanged(SphereGridNodeTypeInfo? value)
    {
        _lastFindIndex = -1;
        OnPropertyChanged(nameof(CanReplaceNodeTypes));
        OnPropertyChanged(nameof(CanReplaceSelectedNode));
        RefreshFindReplaceStatus();
    }

    partial void OnReplacementNodeTypeChanged(SphereGridNodeTypeInfo? value)
    {
        OnPropertyChanged(nameof(CanReplaceNodeTypes));
        OnPropertyChanged(nameof(CanReplaceSelectedNode));
        RefreshFindReplaceStatus();
    }

    partial void OnIsDirtyChanged(bool value) =>
        OnPropertyChanged(nameof(CanUndoAll));

    partial void OnPendingNodeTypeChanged(SphereGridNodeTypeInfo? value)
    {
        CommitSelectedNodeControls();
    }

    partial void OnPendingCharacterChanged(SphereGridCharacter? value)
    {
        CommitSelectedNodeControls();
    }

    partial void OnPendingXChanged(decimal? value)
    {
        CommitSelectedNodeControls();
    }

    partial void OnPendingYChanged(decimal? value)
    {
        CommitSelectedNodeControls();
    }

    partial void OnNewNodeTypeChanged(SphereGridNodeTypeInfo? value) =>
        OnPropertyChanged(nameof(CanAddExperimentalNode));

    partial void OnNewNodeCharacterChanged(SphereGridCharacter value) =>
        OnPropertyChanged(nameof(CanAddExperimentalNode));

    partial void OnSelectedExperimentalLinkIndexChanged(decimal value)
    {
        OnPropertyChanged(nameof(SelectedLinkNumber));
        OnPropertyChanged(nameof(HighlightedExperimentalLinkIndex));
        if (!_updatingLinkControls)
            SelectExperimentalLink(decimal.ToInt32(decimal.Round(value)));
    }

    partial void OnExperimentalLinkHighlightEnabledChanged(bool value) =>
        OnPropertyChanged(nameof(HighlightedExperimentalLinkIndex));

    partial void OnHasSelectedExperimentalLinkChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedLinkNumber));
        OnPropertyChanged(nameof(HighlightedExperimentalLinkIndex));
    }

    partial void OnPendingLinkNodeAChanged(decimal? value)
    {
        RefreshExperimentalLinkState();
        if (CanCommitSelectedLink)
            CommitSelectedLinkEdit();
        OnPropertyChanged(nameof(CanAddExperimentalLink));
    }
    partial void OnPendingLinkNodeBChanged(decimal? value)
    {
        RefreshExperimentalLinkState();
        if (CanCommitSelectedLink)
            CommitSelectedLinkEdit();
        OnPropertyChanged(nameof(CanAddExperimentalLink));
    }
    partial void OnPendingLinkAnchorChanged(decimal? value)
    {
        OnPropertyChanged(nameof(ExperimentalPreviewAnchorIndex));
        RefreshExperimentalLinkState();
        if (CanCommitSelectedLink)
            CommitSelectedLinkEdit();
        OnPropertyChanged(nameof(CanAddExperimentalLink));
    }

    public void SelectExperimentalLink(int index)
    {
        if (Graph is null || Graph.File.Links.Count == 0)
            return;
        index = Math.Clamp(index, 0, Graph.File.Links.Count - 1);
        SphereGridLink link = Graph.File.Links[index];
        _updatingLinkControls = true;
        SelectedExperimentalLinkIndex = index;
        PendingLinkNodeA = link.NodeAIndex;
        PendingLinkNodeB = link.NodeBIndex;
        PendingLinkAnchor = link.AnchorNodeIndex;
        _updatingLinkControls = false;
        HasSelectedExperimentalLink = true;
        RefreshExperimentalLinkState();
    }

    public void ClearGraphSelection()
    {
        SelectedNode = null;
        ClearLinkSelection();
    }

    public void ClearLinkSelection()
    {
        HasSelectedExperimentalLink = false;
        _updatingLinkControls = true;
        PendingLinkNodeA = null;
        PendingLinkNodeB = null;
        PendingLinkAnchor = null;
        _updatingLinkControls = false;
        CanCommitSelectedLink = false;
        OnPropertyChanged(nameof(ExperimentalPreviewAnchorIndex));
        OnPropertyChanged(nameof(CanAddExperimentalLink));
    }

    private void RefreshExperimentalLinkState()
    {
        if (_updatingLinkControls || Graph is null || Graph.File.Links.Count == 0)
        {
            CanCommitSelectedLink = false;
            return;
        }
        int index = decimal.ToInt32(decimal.Round(SelectedExperimentalLinkIndex));
        if (PendingLinkNodeA is not decimal pendingA ||
            PendingLinkNodeB is not decimal pendingB ||
            PendingLinkAnchor is not decimal pendingAnchor)
        {
            CanCommitSelectedLink = false;
            return;
        }
        int a = decimal.ToInt32(decimal.Round(pendingA));
        int b = decimal.ToInt32(decimal.Round(pendingB));
        int anchor = decimal.ToInt32(decimal.Round(pendingAnchor));
        if (index < 0 || index >= Graph.File.Links.Count ||
            a < 0 || a >= Graph.File.Nodes.Count ||
            b < 0 || b >= Graph.File.Nodes.Count || a == b ||
            (anchor != ushort.MaxValue && (anchor < 0 || anchor >= Graph.File.Nodes.Count)))
        {
            CanCommitSelectedLink = false;
            return;
        }
        SphereGridLink current = Graph.File.Links[index];
        if (!HasUsableEndpointCapacity(a, b, index))
        {
            CanCommitSelectedLink = false;
            return;
        }
        CanCommitSelectedLink = current.NodeAIndex != a ||
            current.NodeBIndex != b || current.AnchorNodeIndex != anchor;
    }

    public void BeginNodeDrag(int nodeIndex)
    {
        if (SelectedNode?.Index != nodeIndex || Graph is null ||
            _dragStartSnapshot is not null)
            return;
        EditSession session = _sessions[SelectedGrid];
        _dragStartSnapshot = CaptureSnapshot(session);
        _dragNodeIndex = nodeIndex;
    }

    public void CompleteNodeDrag(int nodeIndex)
    {
        if (_dragStartSnapshot is null || _dragNodeIndex != nodeIndex ||
            !_sessions.TryGetValue(SelectedGrid, out EditSession? session))
        {
            _dragStartSnapshot = null;
            _dragNodeIndex = -1;
            return;
        }

        SphereGridNode before = _dragStartSnapshot.Graph.File.Nodes[nodeIndex];
        SphereGridNode after = session.Graph.File.Nodes[nodeIndex];
        if (before.X != after.X || before.Y != after.Y)
        {
            RecordUndoCheckpoint(session, _dragStartSnapshot,
                $"moved node #{nodeIndex}: ({before.X}, {before.Y}) → ({after.X}, {after.Y})");
            session.DirtyNodes.Add(nodeIndex);
            IsDirty = true;
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanUndoAll));
            UpdateStatus($"Moved node #{nodeIndex} to {after.X}, {after.Y}");
        }
        _dragStartSnapshot = null;
        _dragNodeIndex = -1;
    }

    private void PreviewNodeDragPosition(int nodeIndex, short targetX, short targetY)
    {
        if (_dragStartSnapshot is null || _dragNodeIndex != nodeIndex ||
            SelectedNode?.Index != nodeIndex || Graph is null)
            return;
        if (SelectedNode.X == targetX && SelectedNode.Y == targetY)
            return;

        EditSession session = _sessions[SelectedGrid];
        SphereGridNode[] nodes = session.Graph.File.Nodes.ToArray();
        nodes[nodeIndex] = nodes[nodeIndex] with { X = targetX, Y = targetY };
        session.Graph = new SphereGridGraph(
            CopyWithNodes(session.Graph.File, nodes),
            session.Graph.Routes);
        Graph = session.Graph;
        SelectedNode = session.Graph.File.Nodes[nodeIndex];
    }

    public void PreviewSelectedNodePosition(int nodeIndex, short x, short y)
    {
        if (SelectedNode?.Index != nodeIndex || Graph is null)
            return;

        // Keep the coordinate controls synchronized while the drag transaction
        // records only the final release position as one Undo action.
        _updatingNodeControls = true;
        _updatingCoordinateControls = true;
        PendingX = x;
        PendingY = y;
        _updatingCoordinateControls = false;
        _updatingNodeControls = false;
        PreviewNodeDragPosition(nodeIndex, x, y);
    }

    private void UpdateStatus(string? message = null)
    {
        if (Graph is null)
            return;
        string dirty = IsDirty ? "  ·  Modified" : "";
        Status = message ??
            $"{SelectedGrid}  ·  {Graph.File.Nodes.Count} nodes  ·  " +
            $"{Graph.File.Links.Count} links  ·  {Graph.File.Clusters.Count} clusters" +
            dirty;
    }

    private void RefreshFindReplaceStatus()
    {
        if (Graph is null || FindNodeType is null)
        {
            FindReplaceStatus = "Select a node type to begin.";
            return;
        }
        int count = Graph.VisibleNodes.Count(node => node.Type == FindNodeType.Id);
        string matchWord = count == 1 ? "match" : "matches";
        FindReplaceStatus = count == 0
            ? $"No matches found for {FindNodeType.Name}."
            : $"{count} {matchWord} found for {FindNodeType.Name} in the {SelectedGrid} grid.";
    }

    private static SphereGridFile CopyWithNodes(
        SphereGridFile source,
        IReadOnlyList<SphereGridNode> nodes) =>
        CopyWithGraphData(source, source.Clusters, nodes, source.Links);

    private static SphereGridFile CopyWithGraphData(
        SphereGridFile source,
        IReadOnlyList<SphereGridCluster> clusters,
        IReadOnlyList<SphereGridNode> nodes,
        IReadOnlyList<SphereGridLink> links) =>
        new()
        {
            Kind = source.Kind,
            LayoutPath = source.LayoutPath,
            ContentPath = source.ContentPath,
            OriginalLayoutBytes = source.OriginalLayoutBytes,
            OriginalContentBytes = source.OriginalContentBytes,
            HeaderValue = source.HeaderValue,
            UnknownHeaderValues = source.UnknownHeaderValues,
            Clusters = clusters,
            Nodes = nodes,
            Links = links
        };

    private EditSnapshot CaptureSnapshot(EditSession session) => new(
        session.Graph,
        new Dictionary<int, SphereGridCharacter>(session.ColorOverrides),
        new HashSet<int>(session.DirtyNodes),
        new HashSet<int>(session.DirtyLinks),
        SelectedNode?.Index,
        HasSelectedExperimentalLink,
        HasSelectedExperimentalLink
            ? decimal.ToInt32(decimal.Round(SelectedExperimentalLinkIndex))
            : -1);

    private void RecordUndoCheckpoint(
        EditSession session,
        EditSnapshot? snapshot = null,
        string description = "Sphere Grid change")
    {
        session.UndoHistory.Push((snapshot ?? CaptureSnapshot(session)) with
        {
            Description = description
        });
        session.RedoHistory.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private sealed record EditSnapshot(
        SphereGridGraph Graph,
        IReadOnlyDictionary<int, SphereGridCharacter> ColorOverrides,
        IReadOnlySet<int> DirtyNodes,
        IReadOnlySet<int> DirtyLinks,
        int? SelectedNodeIndex,
        bool HasSelectedLink,
        int SelectedLinkIndex,
        string Description = "Sphere Grid change");

    private sealed class EditSession(
        SphereGridFile sourceFile,
        SphereGridGraph graph)
    {
        public SphereGridFile SourceFile { get; set; } = sourceFile;
        public SphereGridRouteMetadata DefaultRoutes { get; } = graph.Routes;
        public SphereGridGraph Graph { get; set; } = graph;
        public Dictionary<int, SphereGridCharacter> ColorOverrides { get; } = new();
        public Dictionary<int, SphereGridCharacter> SavedColorOverrides { get; } = new();
        public HashSet<int> DirtyNodes { get; } = new();
        public HashSet<int> DirtyLinks { get; } = new();
        public Stack<EditSnapshot> UndoHistory { get; } = new();
        public Stack<EditSnapshot> RedoHistory { get; } = new();
    }
}
