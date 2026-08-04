using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MixEditor;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FFXProjectEditor;

public partial class MixEditor_Control : UserControl
{
    private readonly MixEditor_DataModel DataModel;

    public MixEditor_Control()
    {
        DataModel = new MixEditor_DataModel();
        DataContext = DataModel;
        InitializeComponent();
    }

    private void Filter_Changed(object? sender, TextChangedEventArgs e) => DataModel.ApplyFilter();

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { DataModel.Save(); }
        catch (Exception ex) { DataModel.Status = "ERROR: " + ex.Message; }
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

        string projectPath = Project_Service.Instance.Path_KernelMixRecipes;
        string? originalPath = VanillaReference_Service.ResolveProjectFile(projectPath);
        if (originalPath is null)
        {
            DataModel.Status =
                "ERROR: The configured Original Game Files folder does not contain prepare.bin.";
            return;
        }

        bool confirmed = await AiRevertConfirmationWindow.Show(owner,
            "Restore Original Mix Recipes",
            "This will immediately replace the complete prepare.bin Mix recipe table with its original, " +
            "unedited game file. All recipe changes will be discarded.",
            originalPath, "Restore and Reload",
            "Confirming will immediately validate and write prepare.bin into the active project.");
        if (!confirmed)
        {
            DataModel.Status = "Restore Original was cancelled.";
            return;
        }

        try
        {
            DataModel.RestoreOriginalAndSave(originalPath);
            DataContext = null;
            DataContext = DataModel;
        }
        catch (Exception ex)
        {
            DataModel.Status = "ERROR: " + ex.Message;
        }
    }
}
