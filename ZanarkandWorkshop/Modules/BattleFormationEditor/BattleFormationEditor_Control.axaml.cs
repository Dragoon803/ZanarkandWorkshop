using Avalonia.Controls;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using System;
using System.IO;

namespace FFXProjectEditor.Modules.BattleFormationEditor;

public partial class BattleFormationEditor_Control : UserControl
{
    private bool _restoringFormationSelection;
    private BattleFormationEditor_DataModel Model => (BattleFormationEditor_DataModel)DataContext!;
    public string? SelectedFolderPath =>
        Model.SelectedFile is null ? null : Path.GetDirectoryName(Model.SelectedFile.FullPath);

    public BattleFormationEditor_Control()
    {
        InitializeComponent();
        DataContext = new BattleFormationEditor_DataModel();
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

            if (TopLevel.GetTopLevel(this) is not Window owner)
                return;

            bool discard = await AiRevertConfirmationWindow.ShowWithoutSource(
                owner,
                "Discard Unsaved Battle Formation Changes?",
                "Changing battles will discard every unsaved enemy party and coordinate change in the Battle Formation Editor.",
                "Discard Changes",
                "Choose Cancel to remain on this battle and save your changes first.");
            if (!discard)
                return;
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

    private void PositionSelector_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is ComboBox { SelectedItem: FormationPositionRow position })
            FormationCanvas.CenterOn(position);
    }

    private async void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Model.Save();
        }
        catch (Exception ex)
        {
            await ShowError("Battle formation could not be saved", ex);
        }
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
