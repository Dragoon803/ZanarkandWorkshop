using Avalonia.Controls;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using System;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public partial class TreasureMapEditor_Control : UserControl
{
    private bool _restoringFieldSelection;
    private TreasureMapEditor_DataModel Model => (TreasureMapEditor_DataModel)DataContext!;
    public TreasureMapEditor_Control() : this(new TreasureMapEditor_DataModel()) { }
    public TreasureMapEditor_Control(TreasureMapEditor_DataModel model) { InitializeComponent(); DataContext = model; }
    public bool HasUnsavedChanges => Model.IsDirty;
    private async void FieldList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_restoringFieldSelection || sender is not ListBox list) return;
        TreasureFieldItem? requested = list.SelectedItem as TreasureFieldItem;
        TreasureFieldItem? current = Model.SelectedField;
        if (ReferenceEquals(requested, current)) return;

        if (current is not null && Model.IsDirty)
        {
            _restoringFieldSelection = true;
            list.SelectedItem = current;
            _restoringFieldSelection = false;

            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            bool discard = await AiRevertConfirmationWindow.ShowWithoutSource(
                owner,
                "Discard Unsaved Treasure Changes?",
                "Changing maps will discard every unsaved chest and NPC reward change in the Treasure Map Editor.",
                "Discard Changes",
                "Choose Cancel to remain on this map and save your changes first.");
            if (!discard) return;

            Model.DiscardUnsavedChanges();
        }

        Model.SelectedField = requested;
    }
    private void PreviousChest_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Model.NextChest(-1);
        MapCanvas.CenterOn(Model.SelectedChest);
    }
    private void NextChest_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Model.NextChest(1);
        MapCanvas.CenterOn(Model.SelectedChest);
    }
    private void PreviousMap_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Model.NextModel(-1);
    private void NextMap_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Model.NextModel(1);
    private void ZoomOut_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => MapCanvas.ZoomOut();
    private void ZoomIn_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => MapCanvas.ZoomIn();
    private void Fit_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => MapCanvas.Fit();
    private async void RestoreChestReward_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RestoreReward(Model.SelectedChest?.ActiveReward);
    private async void RestoreNpcReward_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) =>
        await RestoreReward(Model.SelectedNpcReward);

    private async System.Threading.Tasks.Task RestoreReward(NpcTreasureRow? reward)
    {
        if (reward is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            string originalPath = Model.GetOriginalCatalogPath();
            bool confirmed = await AiRevertConfirmationWindow.Show(
                owner,
                $"Restore Treasure #{reward.TreasureId}?",
                "This restores only the selected four-byte reward record. Other chest and NPC reward changes will not be affected.",
                originalPath,
                "Restore Reward",
                "The restored reward remains in memory until you press Save.");
            if (confirmed) Model.RestoreOriginalReward(reward);
        }
        catch (Exception ex)
        {
            if (owner is Main_Window mainOwner)
                await RecoveryNotice_Window.Show(mainOwner, "Reward could not be restored",
                    ex.Message, Model.CatalogPath, false);
            else Model.Status = ex.Message;
        }
    }
    private async void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { Model.Save(); }
        catch (Exception ex)
        {
            if (TopLevel.GetTopLevel(this) is Main_Window owner)
                await RecoveryNotice_Window.Show(owner, "Treasure catalog could not be saved", ex.Message, Model.CatalogPath, false);
            else Model.Status = ex.Message;
        }
    }
}
