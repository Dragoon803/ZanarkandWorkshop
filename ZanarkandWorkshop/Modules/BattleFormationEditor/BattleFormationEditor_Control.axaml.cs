using Avalonia.Controls;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.IO;

namespace FFXProjectEditor.Modules.BattleFormationEditor;

public partial class BattleFormationEditor_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    private bool _restoringFormationSelection;
    private BattleFormationEditor_DataModel Model => (BattleFormationEditor_DataModel)DataContext!;
    public string? SelectedFolderPath =>
        Model.SelectedFile is null ? null : Path.GetDirectoryName(Model.SelectedFile.FullPath);
    public bool HasPendingChanges => Model.IsDirty;
    public bool CanUndo => Model.CanUndo;
    public bool CanRedo => Model.CanRedo;
    public bool CanUndoAll => Model.CanUndoAll;
    public void Undo() => Model.Undo();
    public void Redo() => Model.Redo();
    public void UndoAll() => Model.UndoAll();
    public void Save() => Model.Save();
    public void SaveToMaster(string masterPath, string metadataPath) => Model.SaveToMaster(masterPath);

    public BattleFormationEditor_Control()
        : this(new BattleFormationEditor_DataModel())
    {
    }

    public BattleFormationEditor_Control(BattleFormationEditor_DataModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        DataContext = model;
        FormationCanvas.PositionDragStarted += Model.BeginPositionDrag;
        FormationCanvas.PositionDragPreviewRequested += Model.PreviewPositionDrag;
        FormationCanvas.PositionDragCompleted += Model.CompletePositionDrag;
        FormationCanvas.PositionDragCanceled += Model.CancelPositionDrag;
    }

    private void Rescan_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Model.RefreshFiles();

    public void ReloadAfterFolderRecovery() => Model.ReloadSelected();

    private async void FormationList_SelectionChanged(
        object? sender, SelectionChangedEventArgs e)
    {
        if (_restoringFormationSelection || sender is not ListBox list)
            return;

        BattleFormationFileItem? requested = list.SelectedItem as BattleFormationFileItem;
        BattleFormationFileItem? current = Model.SelectedFile;
        if (ReferenceEquals(requested, current))
            return;

        if (current is not null && Model.IsDirty)
        {
            _restoringFormationSelection = true;
            list.SelectedItem = current;
            _restoringFormationSelection = false;

            if (TopLevel.GetTopLevel(this) is not Main_Window owner)
                return;

            PendingChangesDecision decision = await PendingChanges_Window.Show(owner,
                "Changing battles affects the current formation only.");
            if (decision == PendingChangesDecision.Cancel)
                return;
            if (decision == PendingChangesDecision.Save &&
                !await owner.SaveActiveEditorAsync())
                return;
            if (decision == PendingChangesDecision.Discard)
                Model.ReloadSelected();
        }

        Model.SelectedFile = requested;
    }

    private void ZoomOut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        FormationCanvas.ZoomOut();

    private void ZoomIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        FormationCanvas.ZoomIn();

    private void ZoomFit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        FormationCanvas.Fit();

    private void ResetPosition_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Model.ResetSelectedPosition();
    private void Undo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Undo();
    private void Redo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Redo();
    private void UndoAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoAll();

    private void PositionSelector_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is ComboBox { SelectedItem: FormationPositionRow position })
            FormationCanvas.CenterOn(position);
    }

    private async void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner &&
            !await owner.EnsureActiveProjectRegisteredAsync()) return;
        try
        {
            Model.Save();
        }
        catch (Exception ex)
        {
            await ShowError("Battle formation could not be saved", ex);
        }
    }
    private async void SaveAs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner)
            await owner.SaveAsActiveProjectAsync((master, _) =>
            {
                Model.SaveToMaster(master);
                return System.Threading.Tasks.Task.CompletedTask;
            });
    }

    private async System.Threading.Tasks.Task ShowError(string title, Exception ex)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner)
            await RecoveryNotice_Window.Show(
                owner, title, ex.Message, Model.SelectedFile?.FullPath ?? "", false);
        else
            Model.Status = $"{title}: {ex.Message}";
    }
}
