using Avalonia.Controls;
using Avalonia.Threading;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using System;
using System.ComponentModel;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public partial class TreasureMapEditor_Control : UserControl
{
    private bool _restoringFieldSelection;
    private TreasureMapEditor_DataModel Model => (TreasureMapEditor_DataModel)DataContext!;
    public TreasureMapEditor_Control() : this(new TreasureMapEditor_DataModel()) { }
    public TreasureMapEditor_Control(TreasureMapEditor_DataModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += Model_PropertyChanged;
    }
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
    private void ChestReward_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Keep transient ComboBox clearing during a chest change out of the
        // nested reward model, while still applying deliberate user choices.
        if (sender is ComboBox { SelectedItem: TreasureRewardOption selected } &&
            Model.SelectedChest?.ActiveReward is NpcTreasureRow reward &&
            reward.RewardOptions.Contains(selected))
            reward.SelectedReward = selected;
    }
    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TreasureMapEditor_DataModel.SelectedChest)) return;

        // ItemsSource and SelectedItem are independent bindings. When a chest
        // changes, Avalonia can evaluate SelectedItem against the old item list
        // and leave the control visually empty. Reapply it after both bindings
        // have processed the new chest.
        Dispatcher.UIThread.Post(() =>
        {
            if (Model.SelectedChest?.ActiveReward is NpcTreasureRow reward)
                ChestRewardCombo.SelectedItem = reward.SelectedReward;
        }, DispatcherPriority.DataBind);
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
