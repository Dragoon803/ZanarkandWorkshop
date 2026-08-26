using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.SphereGridEditor;

public partial class SphereGridEditor_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    private bool _gridTabTransitionInProgress;
    private int _committedGridTabIndex = 1;
    private bool _isCreatingLink;
    private int? _newLinkNodeA;
    private int? _newLinkNodeB;

    private SphereGridEditor_DataModel Model =>
        (SphereGridEditor_DataModel)DataContext!;

    public SphereGridEditor_Control()
    {
        InitializeComponent();
        DataContext = new SphereGridEditor_DataModel();
        GridCanvas.NodeSelectionRequested += GridCanvas_NodeSelectionRequested;
        GridCanvas.NodeDragStarted += GridCanvas_NodeDragStarted;
        GridCanvas.NodePositionPreviewRequested += GridCanvas_NodePositionPreviewRequested;
        GridCanvas.NodeDragCompleted += GridCanvas_NodeDragCompleted;
        GridCanvas.LinkSelectionRequested += GridCanvas_LinkSelectionRequested;
        GridCanvas.EmptySpaceSelectionRequested += GridCanvas_EmptySpaceSelectionRequested;
        AddHandler(KeyDownEvent, LinkCreation_KeyDown, RoutingStrategies.Tunnel);
    }

    public async Task RestoreOriginalAsync(Window owner)
    {
        try
        {
            if (!await RecoverySource_Util.EnsureConfiguredAsync(owner))
            { Model.Status = "Restore Original was cancelled."; return; }
        }
        catch (Exception ex) { Model.Status = "Sphere Grid recovery failed: " + ex.Message; return; }

        SphereGridFileSet projectStandard = SphereGridFileSet.FromDirectory(
            Project_Service.Instance.Path_SphereGrid, SphereGridKind.Standard);
        string? originalStandardLayout =
            VanillaReference_Service.ResolveProjectFile(projectStandard.LayoutPath);
        if (originalStandardLayout is null)
        {
            Model.Status = "The configured Original Game Files folder does not contain the Sphere Grid files.";
            return;
        }
        string originalDirectory = Path.GetDirectoryName(originalStandardLayout)!;
        foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
        {
            SphereGridFileSet originalFiles = SphereGridFileSet.FromDirectory(originalDirectory, kind);
            if (!File.Exists(originalFiles.LayoutPath) || !File.Exists(originalFiles.ContentPath))
            {
                Model.Status = $"The configured Original Game Files folder is missing the {kind} Sphere Grid files.";
                return;
            }
        }

        var restoreFiles = new List<RecoveryFileVerification>();
        foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
        {
            SphereGridFileSet projectFiles = SphereGridFileSet.FromDirectory(
                Project_Service.Instance.Path_SphereGrid, kind);
            restoreFiles.Add(VanillaReference_Service.VerifyProjectFile(projectFiles.LayoutPath));
            restoreFiles.Add(VanillaReference_Service.VerifyProjectFile(projectFiles.ContentPath));
        }
        bool confirmed = await AiRevertConfirmationWindow.Show(
            owner,
            "Restore All Original Sphere Grids?",
            "This immediately replaces the Original, Standard, and Expert Sphere Grids with " +
            "the game's clean files. All six project files and every unsaved Sphere Grid edit will be replaced." +
            VanillaReference_Service.BuildRestoreTrustNotice(restoreFiles),
            originalDirectory,
            restoreFiles.Any(file => file.RequiresWarning) ? "Restore Unverified Files" : "Restore and Reload",
            "Confirming immediately writes the original Sphere Grid files into the active editing project.");
        if (!confirmed)
        {
            Model.Status = "Restore Original was cancelled.";
            return;
        }

        try
        {
            var approvedUnverifiedPaths = restoreFiles
                .Where(file => file.RequiresWarning)
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
            {
                SphereGridFileSet projectFiles = SphereGridFileSet.FromDirectory(
                    Project_Service.Instance.Path_SphereGrid, kind);
                RecoveryFileVerification currentLayout =
                    VanillaReference_Service.VerifyProjectFile(projectFiles.LayoutPath);
                RecoveryFileVerification currentContent =
                    VanillaReference_Service.VerifyProjectFile(projectFiles.ContentPath);
                _ = VanillaReference_Service.ResolveAuthorizedProjectFile(
                    projectFiles.LayoutPath,
                    approvedUnverifiedPaths.Contains(currentLayout.RelativePath));
                _ = VanillaReference_Service.ResolveAuthorizedProjectFile(
                    projectFiles.ContentPath,
                    approvedUnverifiedPaths.Contains(currentContent.RelativePath));
            }
            Model.RestoreOriginalAndReload(originalDirectory);
            GridCanvas.Fit();
            await RecoveryNotice_Window.Show(
                owner,
                "Sphere Grids Restored",
                "The Original, Standard, and Expert Sphere Grids were restored and reloaded.",
                Project_Service.Instance.Path_SphereGrid,
                true);
        }
        catch (Exception ex)
        {
            Model.Status = "Sphere Grid recovery failed: " + ex.Message;
            await RecoveryNotice_Window.Show(
                owner,
                "Sphere Grids Could Not Be Restored",
                ex.Message,
                Project_Service.Instance.Path_SphereGrid,
                false);
        }
    }

    private async void GridTabs_SelectionChanged(
        object? sender, SelectionChangedEventArgs e)
    {
        if (_gridTabTransitionInProgress ||
            DataContext is not SphereGridEditor_DataModel model)
            return;

        int requestedIndex = GridTabs.SelectedIndex;
        SphereGridKind kind = requestedIndex switch
        {
            0 => SphereGridKind.Original,
            1 => SphereGridKind.Standard,
            2 => SphereGridKind.Expert,
            _ => SphereGridKind.Standard
        };
        if (model.SelectedGrid == kind)
        {
            _committedGridTabIndex = requestedIndex;
            return;
        }

        _gridTabTransitionInProgress = true;
        try
        {
            // Keep the visible tab synchronized with the grid that is still loaded
            // while the user decides what to do with that grid's private session.
            GridTabs.SelectedIndex = _committedGridTabIndex;

            if (model.IsGridDirty(model.SelectedGrid))
            {
                if (TopLevel.GetTopLevel(this) is not Window owner)
                    return;

                PendingChangesDecision decision = await PendingChanges_Window.Show(
                    owner,
                    $"Switching from the {model.SelectedGrid} grid to the {kind} grid " +
                    $"will leave the current grid. Only {model.SelectedGrid} will be saved or discarded.");

                if (decision == PendingChangesDecision.Cancel)
                    return;

                if (decision == PendingChangesDecision.Save)
                {
                    if (owner is Main_Window mainWindow &&
                        !await mainWindow.EnsureActiveProjectRegisteredAsync())
                        return;
                    try
                    {
                        model.SaveCurrentGrid();
                    }
                    catch (Exception ex)
                    {
                        await ShowSaveError(ex);
                        return;
                    }
                }
                else
                {
                    model.DiscardCurrentGridChanges();
                }
            }

            // The switch is now approved. Keep an unfinished preview intact when
            // the user cancels the decision or when saving cannot complete.
            if (_isCreatingLink)
                CancelLinkCreation("Link creation cancelled because the grid changed.");

            model.Load(kind);
            _committedGridTabIndex = requestedIndex;
            GridTabs.SelectedIndex = requestedIndex;
        }
        finally
        {
            _gridTabTransitionInProgress = false;
        }
    }

    private void ZoomOut_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        GridCanvas.ZoomOut();

    private void ZoomIn_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        GridCanvas.ZoomIn();

    private void Fit_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        GridCanvas.Fit();

    private void FindNext_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SphereGridNode? node = Model.FindNextNode();
        if (node is not null)
            GridCanvas.CenterOn(node);
    }

    private void ReplaceSelected_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Model.ReplaceSelectedNodeType() && Model.SelectedNode is { } node)
            GridCanvas.CenterOn(node);
    }

    private async void ReplaceAll_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Model.FindNodeType is null || Model.ReplacementNodeType is null ||
            Model.FindNodeType.Id == Model.ReplacementNodeType.Id)
        {
            Model.FindReplaceStatus =
                "Choose different node types for Find and Replace with.";
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        bool confirmed = await AiRevertConfirmationWindow.ShowWithoutSource(
            owner,
            $"Replace all {Model.FindNodeType.Name} nodes?",
            $"Every {Model.FindNodeType.Name} node in the current {Model.SelectedGrid} grid " +
            $"will become {Model.ReplacementNodeType.Name}.",
            "Replace All",
            "The replacements remain in memory until Save is pressed and can be discarded with Undo All.");
        if (confirmed)
            Model.ReplaceAllNodeTypes();
    }

    private async void CreateNode_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || Model.SelectedNode is null)
            return;
        if (!await SphereGridCreationCaution_Window.Confirm(owner, "Node"))
            return;
        AddSphereGridNodeResult? result = await AddSphereGridNode_Window.Show(owner, Model);
        if (result is null)
            return;
        Model.NewNodeType = result.NodeType;
        Model.NewNodeCharacter = result.Character;
        Model.NewNodeX = result.X;
        Model.NewNodeY = result.Y;
        SphereGridNode? node = Model.AddExperimentalNode();
        if (node is not null)
            GridCanvas.CenterOn(node);
    }

    private async void CreateLink_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isCreatingLink)
        {
            if (!Model.CanCreateExperimentalLink)
            {
                LinkCreationStatus.Text = "This grid cannot accept another link.";
                return;
            }

            _isCreatingLink = true;
            _newLinkNodeA = null;
            _newLinkNodeB = null;
            GridCanvas.IsLinkCreationMode = true;
            GridCanvas.PreviewLinkNodeAIndex = -1;
            GridCanvas.PreviewLinkNodeBIndex = -1;
            CreateLinkButton.Content = "Confirm Link";
            CreateLinkButton.IsEnabled = false;
            CancelLinkCreationButton.IsVisible = true;
            LinkCreationStatus.Text = "Click the first node for Node A.";
            EditorTabs.SelectedIndex = 1;
            return;
        }

        if (_newLinkNodeA is null || _newLinkNodeB is null)
            return;
        if (!Model.TryValidateNewLink(
                _newLinkNodeA.Value, _newLinkNodeB.Value, ushort.MaxValue,
                out string validationMessage))
        {
            LinkCreationStatus.Text = validationMessage;
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        if (!await SphereGridCreationCaution_Window.Confirm(owner, "Link"))
            return;
        Model.AddExperimentalLink(
            _newLinkNodeA.Value, _newLinkNodeB.Value, ushort.MaxValue);
        CancelLinkCreation("Select a node, then click Create Link.");
    }

    private void CancelLinkCreation_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        CancelLinkCreation(
            "Link creation cancelled. Select a node, then click Create Link.");

    private void LinkCreation_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_isCreatingLink)
            return;
        e.Handled = true;
        CancelLinkCreation(
            "Link creation cancelled. Select a node, then click Create Link.");
    }

    private void CancelLinkCreation(string status)
    {
        _isCreatingLink = false;
        _newLinkNodeA = null;
        _newLinkNodeB = null;
        GridCanvas.IsLinkCreationMode = false;
        GridCanvas.PreviewLinkNodeAIndex = -1;
        GridCanvas.PreviewLinkNodeBIndex = -1;
        CreateLinkButton.Content = "Create Link";
        CreateLinkButton.IsEnabled = true;
        CancelLinkCreationButton.IsVisible = false;
        LinkCreationStatus.Text = status;
    }

    private void EditorTabs_SelectionChanged(
        object? sender, SelectionChangedEventArgs e)
    {
        if (_isCreatingLink && EditorTabs.SelectedIndex != 1)
        {
            EditorTabs.SelectedIndex = 1;
            return;
        }
        if (DataContext is SphereGridEditor_DataModel model)
            model.ExperimentalLinkHighlightEnabled = EditorTabs.SelectedIndex == 1;
    }

    private async void SaveGrid_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Model.IsDirty)
            return;
        if (TopLevel.GetTopLevel(this) is Main_Window registrationOwner &&
            !await registrationOwner.EnsureActiveProjectRegisteredAsync()) return;
        try
        {
            Model.SaveCurrentGrid();
        }
        catch (Exception ex)
        {
            await ShowSaveError(ex);
        }
    }
    public bool HasPendingChanges => Model.HasAnyPendingChanges;
    public bool CanUndo => Model.CanUndo;
    public bool CanRedo => Model.CanRedo;
    public bool CanUndoAll => Model.CanUndoAll;
    public void Undo()
    {
        CancelLinkCreationForHistory();
        Model.UndoLastChange();
    }
    public void Redo()
    {
        CancelLinkCreationForHistory();
        Model.RedoLastChange();
    }
    public void UndoAll()
    {
        CancelLinkCreationForHistory();
        Model.RevertAll();
    }
    public void Save() => Model.SaveCurrentGrid();
    public void SaveToMaster(string masterPath, string metadataPath) => Model.SaveToMaster(masterPath, metadataPath);

    private async void SaveAsGrid_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Model.IsDirty || TopLevel.GetTopLevel(this) is not Main_Window owner) return;
        await owner.SaveAsActiveProjectAsync((master, metadata) =>
        {
            Model.SaveToMaster(master, metadata);
            return Task.CompletedTask;
        });
    }

    private async Task ShowSaveError(Exception ex)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner)
        {
            await RecoveryNotice_Window.Show(
                owner,
                "Sphere Grid could not be saved",
                ex.Message,
                Model.CurrentLayoutPath + Environment.NewLine + Model.CurrentContentPath,
                false);
        }
        else
        {
            Model.Status = "Sphere Grid could not be saved: " + ex.Message;
        }
    }

    private void Undo_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Undo();

    private void Redo_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Redo();

    private void UndoAll_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoAll();

    private void CancelLinkCreationForHistory()
    {
        if (_isCreatingLink)
            CancelLinkCreation("Link creation cancelled by the history action.");
    }

    private void GridCanvas_NodeSelectionRequested(
        object? sender, SphereGridNode node)
    {
        if (_isCreatingLink)
        {
            if (_newLinkNodeA is null)
            {
                if (!Model.TryValidateNewLinkEndpoint(node.Index, out string nodeAMessage))
                {
                    LinkCreationStatus.Text = nodeAMessage + " Choose another Node A.";
                    return;
                }
                _newLinkNodeA = node.Index;
                GridCanvas.PreviewLinkNodeAIndex = node.Index;
                LinkCreationStatus.Text =
                    $"Node A: #{node.Index}. Click a different node for Node B.";
                return;
            }

            int nodeA = _newLinkNodeA.Value;
            if (node.Index == nodeA)
            {
                LinkCreationStatus.Text =
                    $"Node A is already #{nodeA}. Click a different node for Node B.";
                return;
            }
            if (!Model.TryValidateNewLinkEndpoint(node.Index, out string nodeBMessage))
            {
                _newLinkNodeB = null;
                GridCanvas.PreviewLinkNodeBIndex = -1;
                CreateLinkButton.IsEnabled = false;
                LinkCreationStatus.Text = nodeBMessage +
                    $" Node A remains #{nodeA}; choose another Node B or Cancel.";
                return;
            }
            if (!Model.TryValidateNewLink(nodeA, node.Index, ushort.MaxValue,
                    out string validationMessage))
            {
                _newLinkNodeB = null;
                GridCanvas.PreviewLinkNodeBIndex = -1;
                CreateLinkButton.IsEnabled = false;
                LinkCreationStatus.Text = validationMessage;
                return;
            }

            _newLinkNodeB = node.Index;
            GridCanvas.PreviewLinkNodeBIndex = node.Index;
            CreateLinkButton.IsEnabled = true;
            LinkCreationStatus.Text =
                $"Straight link preview: Node #{nodeA} → Node #{node.Index}. Click Confirm Link.";
            EditorTabs.SelectedIndex = 1;
            return;
        }
        if (Model.SelectedNode?.Index == node.Index)
            return;
        Model.ClearLinkSelection();
        Model.SelectedNode = Model.Graph?.File.Nodes[node.Index];
        EditorTabs.SelectedIndex = 0;
    }

    private void GridCanvas_LinkSelectionRequested(object? sender, int linkIndex)
    {
        Model.SelectedNode = null;
        Model.SelectExperimentalLink(linkIndex);
        EditorTabs.SelectedIndex = 1;
    }

    private void GridCanvas_NodePositionPreviewRequested(
        object? sender, NodePositionPreviewEventArgs e)
    {
        Model.PreviewSelectedNodePosition(e.NodeIndex, e.X, e.Y);
    }

    private void GridCanvas_NodeDragStarted(object? sender, int nodeIndex) =>
        Model.BeginNodeDrag(nodeIndex);

    private void GridCanvas_NodeDragCompleted(object? sender, int nodeIndex) =>
        Model.CompleteNodeDrag(nodeIndex);

    private void GridCanvas_EmptySpaceSelectionRequested(
        object? sender, EventArgs e)
    {
        Model.ClearGraphSelection();
    }
}
