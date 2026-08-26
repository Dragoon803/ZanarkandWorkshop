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

public partial class MixEditor_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    private readonly MixEditor_DataModel DataModel;

    public MixEditor_Control()
    {
        DataModel = new MixEditor_DataModel();
        DataContext = DataModel;
        InitializeComponent();
    }

    private void Filter_Changed(object? sender, TextChangedEventArgs e) => DataModel.ApplyFilter();
    public bool HasPendingChanges => DataModel.IsDirty;
    public bool CanUndo => DataModel.CanUndo;
    public bool CanRedo => DataModel.CanRedo;
    public bool CanUndoAll => DataModel.CanUndoAll;
    public void Undo() => DataModel.Undo();
    public void Redo() => DataModel.Redo();
    public void UndoAll() => DataModel.UndoAll();
    public void Save() => DataModel.Save();
    public void SaveToMaster(string masterPath, string metadataPath) => DataModel.SaveToMaster(masterPath);

    private async void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner &&
            !await owner.EnsureActiveProjectRegisteredAsync()) return;
        try { DataModel.Save(); }
        catch (Exception ex) { DataModel.Status = "ERROR: " + ex.Message; }
    }
    private void Undo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Undo();
    private void Redo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Redo();
    private void UndoAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => UndoAll();
    private async void SaveAs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner)
            await owner.SaveAsActiveProjectAsync((master, _) =>
            {
                DataModel.SaveToMaster(master);
                return System.Threading.Tasks.Task.CompletedTask;
            });
    }

    public async Task RestoreOriginalAsync(Window owner)
    {
        try
        {
            if (!await RecoverySource_Util.EnsureConfiguredAsync(owner))
            { DataModel.Status = "Restore Original was cancelled."; return; }
        }
        catch (Exception ex) { DataModel.Status = "ERROR: " + ex.Message; return; }

        string projectPath = Project_Service.Instance.Path_KernelMixRecipes;
        string? originalPath = VanillaReference_Service.ResolveProjectFile(projectPath);
        if (originalPath is null)
        {
            DataModel.Status =
                "ERROR: The configured Original Game Files folder does not contain prepare.bin.";
            return;
        }

        RecoveryFileVerification verification = VanillaReference_Service.VerifyProjectFile(projectPath);
        bool confirmed = await AiRevertConfirmationWindow.Show(owner,
            "Restore Original Mix Recipes",
            "This will immediately replace the complete prepare.bin Mix recipe table with its original, " +
            "unedited game file. All recipe changes will be discarded." +
            VanillaReference_Service.BuildRestoreTrustNotice(
                [verification]),
            originalPath,
            verification.RequiresWarning
                ? "Restore Unverified File" : "Restore and Reload",
            "Confirming will immediately validate and write prepare.bin into the active project.");
        if (!confirmed)
        {
            DataModel.Status = "Restore Original was cancelled.";
            return;
        }

        try
        {
            originalPath = VanillaReference_Service.ResolveAuthorizedProjectFile(
                projectPath, verification.RequiresWarning);
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
