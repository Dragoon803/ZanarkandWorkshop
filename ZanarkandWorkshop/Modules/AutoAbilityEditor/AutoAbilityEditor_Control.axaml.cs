using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.AutoAbilityEditor;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;

namespace FFXProjectEditor;

public partial class AutoAbilityEditor_Control : UserControl
{
    private readonly AutoAbilityEditor_DataModel DataModel;
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
    private void Button_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { DataModel.Save(); }
        catch (Exception ex) { DataModel.Status = "ERROR: " + ex.Message; }
    }

    private async void Button_RestoreOriginalAbility(
        object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataModel.SelectedAbility is not AutoAbilityEntry selected ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        try
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
                    DataModel.Status = "Restore Original Ability was cancelled.";
                    return;
                }
                string? selectedPath = folders[0].TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(selectedPath))
                    throw new InvalidOperationException("No local folder was selected.");
                VanillaReference_Service.Configure(selectedPath);
            }

            string? originalAbilityPath = VanillaReference_Service.ResolveProjectFile(
                Project_Service.Instance.Path_KernelAutoAbilityUs);
            if (originalAbilityPath == null)
                throw new FileNotFoundException(
                    "The configured Original Game Files folder does not contain a_ability.bin.");
            string? originalRecipePath = VanillaReference_Service.ResolveProjectFile(
                Project_Service.Instance.Path_KernelCustomization);

            bool confirmed = await AiRevertConfirmationWindow.Show(
                owner,
                $"Restore Original {selected.Name}?",
                "This restores only the selected Auto Ability's properties and all associated text. " +
                "If matching project and original recipes exist, that recipe is restored too. " +
                "Every other Auto Ability and recipe remains unchanged.",
                originalAbilityPath,
                "Restore Ability",
                "The restored data remains in memory until you press Save.");
            if (!confirmed)
            {
                DataModel.Status = "Restore Original Ability was cancelled.";
                return;
            }

            DataModel.RestoreSelectedOriginalAbility(originalAbilityPath, originalRecipePath);
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
                DataModel.Status = "Restore Original was cancelled.";
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
                DataModel.Status = "ERROR: " + ex.Message;
                return;
            }
        }

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
        bool confirmed = await AiRevertConfirmationWindow.Show(owner, $"Restore Original {tabName}",
            explanation, originalPath, "Restore and Reload",
            $"Confirming will immediately validate and write the original {fileName} into the active project.");
        if (!confirmed)
        {
            DataModel.Status = "Restore Original was cancelled.";
            return;
        }

        try
        {
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
