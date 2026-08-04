using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.SphereGridEditor;

public partial class SphereGridEditor_Control : UserControl
{
    private bool _changingGridTabs;

    private SphereGridEditor_DataModel Model =>
        (SphereGridEditor_DataModel)DataContext!;

    public SphereGridEditor_Control()
    {
        InitializeComponent();
        DataContext = new SphereGridEditor_DataModel();
        GridCanvas.NodeSelectionRequested += GridCanvas_NodeSelectionRequested;
        GridCanvas.LinkSelectionRequested += GridCanvas_LinkSelectionRequested;
        GridCanvas.EmptySpaceSelectionRequested += GridCanvas_EmptySpaceSelectionRequested;
    }

    public async Task RestoreOriginalAsync(Window owner)
    {
        if (!VanillaReference_Service.TryValidate(VanillaReference_Service.MasterPath, out _))
        {
            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select your clean, unedited FFX Original Game Files folder",
                    AllowMultiple = false
                });
            if (folders.Count == 0)
            {
                Model.Status = "Restore Original was cancelled.";
                return;
            }

            try
            {
                string? selectedPath = folders[0].TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(selectedPath))
                    throw new InvalidOperationException("No local folder was selected.");
                VanillaReference_Service.Configure(selectedPath);
            }
            catch (Exception ex)
            {
                Model.Status = "Sphere Grid recovery failed: " + ex.Message;
                return;
            }
        }

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

        bool confirmed = await AiRevertConfirmationWindow.Show(
            owner,
            "Restore All Original Sphere Grids?",
            "This immediately replaces the Original, Standard, and Expert Sphere Grids with " +
            "the game's clean files. All six project files and every unsaved Sphere Grid edit will be replaced.",
            originalDirectory,
            "Restore and Reload",
            "Confirming immediately writes the original Sphere Grid files into the active editing project.");
        if (!confirmed)
        {
            Model.Status = "Restore Original was cancelled.";
            return;
        }

        try
        {
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
        if (_changingGridTabs ||
            DataContext is not SphereGridEditor_DataModel model)
            return;
        SphereGridKind kind = GridTabs.SelectedIndex switch
        {
            0 => SphereGridKind.Original,
            1 => SphereGridKind.Standard,
            2 => SphereGridKind.Expert,
            _ => SphereGridKind.Standard
        };
        if (model.SelectedGrid == kind)
            return;
        if (!await ConfirmDiscardPreview(model))
        {
            _changingGridTabs = true;
            GridTabs.SelectedIndex = model.SelectedGrid switch
            {
                SphereGridKind.Original => 0,
                SphereGridKind.Standard => 1,
                SphereGridKind.Expert => 2,
                _ => 1
            };
            _changingGridTabs = false;
            return;
        }
        model.DiscardPreview();
        model.Load(kind);
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

    private async void FindNext_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!await ConfirmDiscardPreview(Model))
            return;
        Model.DiscardPreview();
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
        if (!await ConfirmDiscardPreview(Model) ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;
        Model.DiscardPreview();
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

    private void ApplyNode_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Model.ApplySelectedNode();

    private async void CreateNode_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || Model.SelectedNode is null)
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

    private void ApplyExperimentalLink_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Model.ApplyExperimentalLink();

    private async void CreateLink_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        AddSphereGridLinkResult? result = await AddSphereGridLink_Window.Show(owner, Model);
        if (result is not null)
            Model.AddExperimentalLink(result.NodeA, result.NodeB, result.Anchor);
    }

    private void EditorTabs_SelectionChanged(
        object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SphereGridEditor_DataModel model)
            model.ExperimentalLinkHighlightEnabled = EditorTabs.SelectedIndex == 1;
    }

    private async void SaveGrid_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Model.IsDirty)
            return;
        if (Model.HasPreview)
        {
            await ShowSaveError(new InvalidOperationException(
                "Apply or discard the current position preview before saving."));
            return;
        }
        try
        {
            Model.SaveCurrentGrid();
        }
        catch (Exception ex)
        {
            await ShowSaveError(ex);
        }
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
        object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Model.UndoLastChange();

    private async void UndoAll_Click(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Model.IsDirty && !Model.HasPreview)
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        bool confirmed = await AiRevertConfirmationWindow.ShowWithoutSource(
            owner,
            "Undo All Sphere Grid Changes?",
            "This discards every change made to the current grid since the last save.",
            "Undo All",
            "No game files have been written. This only clears the current editing session.");
        if (confirmed)
            Model.RevertAll();
    }

    private async void GridCanvas_NodeSelectionRequested(
        object? sender, SphereGridNode node)
    {
        if (Model.SelectedNode?.Index == node.Index)
            return;
        if (!await ConfirmDiscardPreview(Model))
            return;
        Model.DiscardPreview();
        Model.ClearLinkSelection();
        Model.SelectedNode = Model.Graph?.File.Nodes[node.Index];
        EditorTabs.SelectedIndex = 0;
    }

    private async void GridCanvas_LinkSelectionRequested(object? sender, int linkIndex)
    {
        if (!await ConfirmDiscardPreview(Model))
            return;
        Model.DiscardPreview();
        Model.SelectedNode = null;
        Model.SelectExperimentalLink(linkIndex);
        EditorTabs.SelectedIndex = 1;
    }

    private async void GridCanvas_EmptySpaceSelectionRequested(
        object? sender, EventArgs e)
    {
        if (!await ConfirmDiscardPreview(Model))
            return;
        Model.DiscardPreview();
        Model.ClearGraphSelection();
    }

    private async Task<bool> ConfirmDiscardPreview(
        SphereGridEditor_DataModel model)
    {
        if (!model.HasPreview)
            return true;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;
        return await AiRevertConfirmationWindow.Show(
            owner,
            "Discard Position Preview?",
            "The selected node has a position preview that has not been applied.",
            model.SelectedNode is null
                ? model.SelectedGrid.ToString()
                : $"Node #{model.SelectedNode.Index}",
            "Discard Preview",
            "Choose Cancel to keep editing, or Discard Preview to abandon the unapplied position.");
    }
}
