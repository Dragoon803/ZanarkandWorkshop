using Avalonia.Controls;
using Avalonia.Threading;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.ComponentModel;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public partial class TreasureMapEditor_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    private bool _restoringFieldSelection;
    private bool _changingChest;
    private int _chestRefreshGeneration;
    private TreasureMapEditor_DataModel Model => (TreasureMapEditor_DataModel)DataContext!;
    public TreasureMapEditor_Control() : this(new TreasureMapEditor_DataModel()) { }
    public TreasureMapEditor_Control(TreasureMapEditor_DataModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += Model_PropertyChanged;
    }
    public bool HasUnsavedChanges => Model.IsDirty;
    public bool HasPendingChanges => Model.IsDirty;
    public bool CanUndo => Model.CanUndo;
    public bool CanRedo => Model.CanRedo;
    public bool CanUndoAll => Model.CanUndoAll;
    public void Undo() => Model.Undo();
    public void Redo() => Model.Redo();
    public void UndoAll() => Model.UndoAll();
    public void Save() => Model.Save();
    public void SaveToMaster(string masterPath, string metadataPath) => Model.SaveToMaster(masterPath);
    private async void FieldList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_restoringFieldSelection || sender is not ListBox list) return;
        TreasureFieldItem? requested = list.SelectedItem as TreasureFieldItem;
        TreasureFieldItem? current = Model.SelectedField;
        if (Model.IsApplyingFieldFilter)
        {
            // Collection reconciliation can briefly move the selected container.
            // Filtering is navigation-only, so retain the loaded map without a
            // pending-changes prompt.
            _restoringFieldSelection = true;
            list.SelectedItem = current;
            _restoringFieldSelection = false;
            return;
        }
        if (ReferenceEquals(requested, current)) return;

        if (current is not null && Model.IsDirty)
        {
            _restoringFieldSelection = true;
            list.SelectedItem = current;
            _restoringFieldSelection = false;

            if (TopLevel.GetTopLevel(this) is not Main_Window owner) return;
            PendingChangesDecision decision = await PendingChanges_Window.Show(owner,
                "Changing maps affects the current Treasure Map editing session.");
            if (decision == PendingChangesDecision.Cancel) return;
            if (decision == PendingChangesDecision.Save &&
                !await owner.SaveActiveEditorAsync()) return;
            if (decision == PendingChangesDecision.Discard)
                Model.DiscardUnsavedChanges();
        }

        Model.SelectedField = requested;
    }
    private void PreviousChest_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        BeginChestNavigation();
        Model.NextChest(-1);
        MapCanvas.CenterOn(Model.SelectedChest);
    }
    private void NextChest_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        BeginChestNavigation();
        Model.NextChest(1);
        MapCanvas.CenterOn(Model.SelectedChest);
    }
    private void BeginChestNavigation()
    {
        _changingChest = true;
        int generation = ++_chestRefreshGeneration;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_chestRefreshGeneration == generation)
                    _changingChest = false;
            },
            DispatcherPriority.Background);
    }
    private void ChestActiveReward_SelectionChanged(
        object? sender, SelectionChangedEventArgs e)
    {
        if (_changingChest || sender is not ComboBox { SelectedItem: NpcTreasureRow selected } ||
            Model.SelectedChest is not TreasureChestRow chest ||
            !chest.AvailableRewards.Contains(selected))
            return;
        BeginChestNavigation();
        chest.ActiveReward = selected;
    }
    private void ChestKind_SelectionChanged(
        object? sender, SelectionChangedEventArgs e)
    {
        if (_changingChest || sender is not ComboBox { SelectedItem: TreasureKind selected } ||
            Model.SelectedChest?.ActiveReward is not NpcTreasureRow reward)
            return;
        reward.SelectedKind = selected;
    }
    private void ChestQuantity_ValueChanged(
        object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_changingChest || sender is not NumericUpDown { Value: decimal value } ||
            Model.SelectedChest?.ActiveReward is not NpcTreasureRow reward)
            return;
        reward.Quantity = checked((byte)decimal.ToInt32(value));
    }
    private void ChestReward_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Keep transient ComboBox clearing during a chest change out of the
        // nested reward model, while still applying deliberate user choices.
        if (!_changingChest &&
            sender is ComboBox { SelectedItem: TreasureRewardOption selected } &&
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
    private void Undo_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Undo();
    private void Redo_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Redo();
    private void UndoAll_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e) => UndoAll();
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
            if (!await RecoverySource_Util.EnsureConfiguredAsync(owner))
            { Model.Status = "Restore Original was cancelled."; return; }
            string originalPath = Model.GetOriginalCatalogPath();
            RecoveryFileVerification verification = VanillaReference_Service.VerifyProjectFile(Model.CatalogPath);
            bool confirmed = await AiRevertConfirmationWindow.Show(
                owner,
                $"Restore Treasure #{reward.TreasureId}?",
                "This restores only the selected four-byte reward record. Other chest and NPC reward changes will not be affected." +
                VanillaReference_Service.BuildRestoreTrustNotice([verification]),
                originalPath,
                verification.RequiresWarning ? "Restore Unverified and Save" : "Restore and Save",
                "Confirming immediately validates and saves the restored reward to the active project.");
            if (confirmed)
            {
                originalPath = VanillaReference_Service.ResolveAuthorizedProjectFile(
                    Model.CatalogPath, verification.RequiresWarning);
                Model.RestoreOriginalRewardAndSave(reward, originalPath);
            }
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
        if (TopLevel.GetTopLevel(this) is Main_Window registrationOwner &&
            !await registrationOwner.EnsureActiveProjectRegisteredAsync()) return;
        try { Model.Save(); }
        catch (Exception ex)
        {
            if (TopLevel.GetTopLevel(this) is Main_Window owner)
                await RecoveryNotice_Window.Show(owner, "Treasure catalog could not be saved", ex.Message, Model.CatalogPath, false);
            else Model.Status = ex.Message;
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
}
