using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor;

public partial class KernelCommands_Control : UserControl
{
    KernelCommands_DataModel DataModel;
    public KernelCommands_Control(CommandFile_enum commandFileType)
    {
        DataModel = new KernelCommands_DataModel(commandFileType);
        this.DataContext = DataModel;
        InitializeComponent();
        SetIdentityColumnsReadOnly();
        DataModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(KernelCommands_DataModel.ShowDescription))
                SetIdentityColumnsReadOnly();
        };
    }

    private void SetIdentityColumnsReadOnly()
    {
        foreach (DataGridColumn column in DGrid.Columns.Where(column =>
                     column.Header?.ToString() is "Index" or "Name" or "Description"))
        {
            column.IsReadOnly = !DataModel.ShowDescription;
        }

        DGrid.FrozenColumnCount = DataModel.ShowDescription ? 3 : 2;
    }

    private void Button_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            DataModel.Save();
        }
        catch (Exception ex)
        {
            DataModel.RecoveryStatus = "ERROR: " + ex.Message;
        }
    }
    private void Button_LoadIngame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LoadInGame();
    }

    private void Filter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        DataModel.ApplyFilter();
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
                DataModel.RecoveryStatus = "Restore Original was cancelled.";
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
                DataModel.RecoveryStatus = "ERROR: " + ex.Message;
                return;
            }
        }

        string projectPath = DataModel.GetFilePath();
        string? originalPath = VanillaReference_Service.ResolveProjectFile(projectPath);
        if (originalPath == null)
        {
            DataModel.RecoveryStatus =
                "ERROR: The configured Original Game Files folder does not contain the matching kernel file.";
            return;
        }

        string editorName = DataModel.GetEditorName();
        string explanation =
            $"This will immediately replace the complete {editorName} file with its original, unedited game file.\n\n" +
            "Every entry, name, description, property, target rule, animation setting, damage value, status effect, " +
            "menu setting, and other field in this editor will be restored. All current modifications in this file " +
            "will be discarded. A one-time .bak captures the project file as it existed before its first save or restore.";
        bool confirmed = await AiRevertConfirmationWindow.Show(owner, $"Restore Original {editorName}",
            explanation, originalPath, "Restore and Save",
            "Confirming will immediately validate and write the original file into the active project.");
        if (!confirmed)
        {
            DataModel.RecoveryStatus = "Restore Original was cancelled.";
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
            DataModel.RecoveryStatus = "ERROR: " + ex.Message;
        }
    }
}
