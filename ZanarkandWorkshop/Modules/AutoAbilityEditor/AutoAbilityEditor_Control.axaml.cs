using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.AutoAbilityEditor;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;

namespace FFXProjectEditor;

public partial class AutoAbilityEditor_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    private readonly AutoAbilityEditor_DataModel DataModel;
    private AutoAbilityEntry? _lastAbilitySelection;
    private bool _selectionTransitionInProgress;
    public AutoAbilityEditor_Control()
    {
        DataModel = new AutoAbilityEditor_DataModel();
        DataContext = DataModel;
        InitializeComponent();
        AddHandler(InputElement.PointerReleasedEvent, (_, _) => QueueDirtyRefresh(),
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(InputElement.KeyUpEvent, (_, _) => QueueDirtyRefresh(),
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }
    private void QueueDirtyRefresh() =>
        Dispatcher.UIThread.Post(DataModel.RefreshDirtyState, DispatcherPriority.Background);
    private void Filter_Changed(object? sender, TextChangedEventArgs e) => DataModel.ApplyFilter();

    private async void AbilityList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectionTransitionInProgress || AbilityList.SelectedItem is not AutoAbilityEntry selected)
            return;
        if (_lastAbilitySelection is null)
        {
            _lastAbilitySelection = selected;
            return;
        }
        if (selected.Id == _lastAbilitySelection.Id)
        {
            _lastAbilitySelection = selected;
            return;
        }

        DataModel.RefreshDirtyState();
        if (!DataModel.IsDirty)
        {
            _lastAbilitySelection = selected;
            return;
        }

        ushort requestedId = selected.Id;
        AutoAbilityEntry previous = _lastAbilitySelection;
        _selectionTransitionInProgress = true;
        try
        {
            AbilityList.SelectedItem = previous;
            if (TopLevel.GetTopLevel(this) is not Main_Window owner) return;
            PendingChangesDecision decision = await PendingChanges_Window.Show(owner,
                "Selecting another Auto Ability will replace the current editing context.");
            if (decision == PendingChangesDecision.Cancel) return;
            if (decision == PendingChangesDecision.Save && !await owner.SaveActiveEditorAsync()) return;
            if (decision == PendingChangesDecision.Discard) DataModel.Load();

            AutoAbilityEntry? requested = DataModel.AllAbilities.FirstOrDefault(ability => ability.Id == requestedId);
            if (requested is null) return;
            DataModel.SelectedAbility = requested;
            AbilityList.SelectedItem = requested;
            AbilityList.ScrollIntoView(requested);
            _lastAbilitySelection = requested;
        }
        finally { _selectionTransitionInProgress = false; }
    }
    public bool HasPendingChanges
    {
        get
        {
            // Pointer/key refreshes are queued so bound controls can commit first.
            // A navigation click can otherwise reach the shell before that queued
            // refresh and incorrectly report this editor as clean.
            DataModel.RefreshDirtyState();
            return DataModel.IsDirty;
        }
    }
    public void Save() => DataModel.Save();
    public bool CanUndo => DataModel.CanUndo;
    public bool CanRedo => DataModel.CanRedo;
    public bool CanUndoAll => DataModel.CanUndoAll;
    public void Undo() => DataModel.Undo();
    public void Redo() => DataModel.Redo();
    public void UndoAll() => DataModel.UndoAll();
    private void Button_Undo(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => DataModel.Undo();
    private void Button_Redo(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => DataModel.Redo();
    private void Button_UndoAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => DataModel.UndoAll();
    public void SaveToMaster(string masterPath, string metadataPath) => DataModel.SaveToMaster(masterPath);
    private async void Button_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner &&
            !await owner.EnsureActiveProjectRegisteredAsync()) return;
        try { DataModel.Save(); }
        catch (Exception ex) { DataModel.Status = "ERROR: " + ex.Message; }
    }
    private async void Button_SaveAs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner)
            await owner.SaveAsActiveProjectAsync((master, _) =>
            {
                DataModel.SaveToMaster(master);
                return System.Threading.Tasks.Task.CompletedTask;
            });
    }

    private async void Button_RestoreOriginalAbility(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataModel.SelectedAbility is not AutoAbilityEntry selected ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        try
        {
            if (!await RecoverySource_Util.EnsureConfiguredAsync(owner))
            { DataModel.Status = "Restore Original Ability was cancelled."; return; }

            string? originalAbilityPath = VanillaReference_Service.ResolveProjectFile(
                Project_Service.Instance.Path_KernelAutoAbilityUs);
            if (originalAbilityPath == null)
                throw new FileNotFoundException(
                    "The configured Original Game Files folder does not contain a_ability.bin.");
            string? originalRecipePath = VanillaReference_Service.ResolveProjectFile(
                Project_Service.Instance.Path_KernelCustomization);

            var restoreFiles = new List<RecoveryFileVerification>
            {
                VanillaReference_Service.VerifyProjectFile(Project_Service.Instance.Path_KernelAutoAbilityUs)
            };
            if (originalRecipePath is not null)
                restoreFiles.Add(VanillaReference_Service.VerifyProjectFile(
                    Project_Service.Instance.Path_KernelCustomization));
            bool confirmed = await AiRevertConfirmationWindow.Show(
                owner,
                $"Restore Original {selected.Name}?",
                "This restores only the selected Auto Ability's properties and all associated text. " +
                "If matching project and original recipes exist, that recipe is restored too. " +
                "Every other Auto Ability and recipe remains unchanged." +
                VanillaReference_Service.BuildRestoreTrustNotice(restoreFiles),
                originalAbilityPath,
                restoreFiles.Any(file => file.RequiresWarning) ? "Restore Unverified and Save" : "Restore and Save",
                "Confirming immediately validates and saves the restored ability data to the active project.");
            if (!confirmed)
            {
                DataModel.Status = "Restore Original Ability was cancelled.";
                return;
            }

            var approvedUnverifiedPaths = restoreFiles
                .Where(file => file.RequiresWarning)
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            RecoveryFileVerification currentAbility = VanillaReference_Service.VerifyProjectFile(
                Project_Service.Instance.Path_KernelAutoAbilityUs);
            originalAbilityPath = VanillaReference_Service.ResolveAuthorizedProjectFile(
                Project_Service.Instance.Path_KernelAutoAbilityUs,
                approvedUnverifiedPaths.Contains(currentAbility.RelativePath));
            if (originalRecipePath is not null)
            {
                RecoveryFileVerification currentRecipe = VanillaReference_Service.VerifyProjectFile(
                    Project_Service.Instance.Path_KernelCustomization);
                originalRecipePath = VanillaReference_Service.ResolveAuthorizedProjectFile(
                    Project_Service.Instance.Path_KernelCustomization,
                    approvedUnverifiedPaths.Contains(currentRecipe.RelativePath));
            }
            DataModel.RestoreSelectedOriginalAbilityAndSave(originalAbilityPath, originalRecipePath);
            DataContext = null;
            DataContext = DataModel;
            EditorTabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            DataModel.Status = "ERROR: " + ex.Message;
        }
    }

    public async Task RestoreOriginalAsync(Window owner)
    {
        try
        {
            if (!await RecoverySource_Util.EnsureConfiguredAsync(owner))
            { DataModel.Status = "Restore Original was cancelled."; return; }
        }
        catch (Exception ex) { DataModel.Status = "ERROR: " + ex.Message; return; }

        bool restoreAbilityFile = EditorTabs.SelectedIndex == 0;
        string projectPath = restoreAbilityFile
            ? Project_Service.Instance.Path_KernelAutoAbilityUs
            : Project_Service.Instance.Path_KernelCustomization;
        string fileName = restoreAbilityFile ? "a_ability.bin" : "kaizou.bin";
        string tabName = restoreAbilityFile ? "Properties & Effects" : "Recipe";
        string? originalPath = VanillaReference_Service.ResolveProjectFile(projectPath);
        if (originalPath == null)
        {
            DataModel.Status =
                $"ERROR: The configured Original Game Files folder does not contain the matching {fileName}.";
            return;
        }

        string explanation =
            $"This will immediately replace the complete {fileName} used by the {tabName} tab with its original, " +
            "unedited game file.\n\nAll changes in that file will be discarded. Reloading the editor will also discard " +
            "any unsaved changes in the other Auto Ability tab.";
        RecoveryFileVerification verification = VanillaReference_Service.VerifyProjectFile(projectPath);
        explanation += VanillaReference_Service.BuildRestoreTrustNotice([verification]);
        bool confirmed = await AiRevertConfirmationWindow.Show(owner, $"Restore Original {tabName}",
            explanation, originalPath, verification.RequiresWarning ? "Restore Unverified File" : "Restore and Reload",
            $"Confirming will immediately validate and write the original {fileName} into the active project.");
        if (!confirmed)
        {
            DataModel.Status = "Restore Original was cancelled.";
            return;
        }

        try
        {
            originalPath = VanillaReference_Service.ResolveAuthorizedProjectFile(
                projectPath, verification.RequiresWarning);
            DataModel.RestoreOriginalAndSave(originalPath, restoreAbilityFile);
            DataContext = null;
            DataContext = DataModel;
            EditorTabs.SelectedIndex = restoreAbilityFile ? 0 : 1;
        }
        catch (Exception ex)
        {
            DataModel.Status = "ERROR: " + ex.Message;
        }
    }
}
