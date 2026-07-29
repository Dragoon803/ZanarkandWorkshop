using Avalonia.Controls;
using FFXProjectEditor.Modules.Main;
using System;
using System.IO;

namespace FFXProjectEditor.Modules.BattleFormationEditor;

public partial class BattleFormationEditor_Control : UserControl
{
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

    private void ZoomOut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        FormationCanvas.ZoomOut();

    private void ZoomIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        FormationCanvas.ZoomIn();

    private void ZoomFit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        FormationCanvas.Fit();

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
