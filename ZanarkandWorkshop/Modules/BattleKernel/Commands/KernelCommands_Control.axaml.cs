using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor;

public partial class KernelCommands_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    KernelCommands_DataModel DataModel;
    public KernelCommands_Control(CommandFile_enum commandFileType)
    {
        DataModel = new KernelCommands_DataModel(commandFileType);
        this.DataContext = DataModel;
        InitializeComponent();
        SetIdentityColumnsReadOnly();
        DGrid.AddHandler(TextBox.TextChangedEvent, DGrid_TextChanged, RoutingStrategies.Bubble);
        DGrid.AddHandler(InputElement.LostFocusEvent, DGrid_EditorCompleted, RoutingStrategies.Bubble);
        DGrid.AddHandler(ToggleButton.IsCheckedChangedEvent, DGrid_EditorCompleted, RoutingStrategies.Bubble);
        DGrid.AddHandler(SelectingItemsControl.SelectionChangedEvent, DGrid_EditorCompleted, RoutingStrategies.Bubble);
        ViewOptionsPanel.AddHandler(
            ToggleButton.IsCheckedChangedEvent,
            ViewOption_Changed,
            RoutingStrategies.Bubble);
        DGrid.CellEditEnded += (_, _) =>
            Dispatcher.UIThread.Post(() => DataModel.RefreshDirtyState(), DispatcherPriority.Background);
        DataModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(KernelCommands_DataModel.ShowDescription))
                SetIdentityColumnsReadOnly();
        };
    }

    private void SetIdentityColumnsReadOnly()
    {
        foreach (DataGridColumn column in DGrid.Columns)
        {
            string? header = column.Header?.ToString();
            if (header == "Index")
                column.IsReadOnly = true;
            else if (header is "Name" or "Description")
                column.IsReadOnly = !DataModel.ShowDescription;
        }

        DGrid.FrozenColumnCount = DataModel.ShowDescription ? 3 : 2;
    }

    private async void Button_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner &&
            !await owner.EnsureActiveProjectRegisteredAsync()) return;
        try
        {
            DataModel.Save();
        }
        catch (Exception ex)
        {
            DataModel.RecoveryStatus = "ERROR: " + ex.Message;
        }
    }

    private void DGrid_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // DataGrid text bindings normally commit only after the cell edit ends.
        // Enable Save as soon as the user types; the CellEditEnded callback then
        // compares the committed file image with the baseline, including nested
        // Status, Duration, and ExtraInfo numeric properties.
        if (e.Source is TextBox { IsFocused: true })
            DataModel.MarkActiveCellDirty();
    }

    private void DGrid_EditorCompleted(object? sender, RoutedEventArgs e)
    {
        // Avalonia can keep several controls in one DataGrid row transaction.
        // Capture each completed editor interaction independently so three
        // edits in one command produce three Undo steps rather than one.
        bool completedEditor = e.Source switch
        {
            TextBox => e.RoutedEvent == InputElement.LostFocusEvent,
            CheckBox => e.RoutedEvent == ToggleButton.IsCheckedChangedEvent,
            ComboBox => e.RoutedEvent == SelectingItemsControl.SelectionChangedEvent,
            _ => false
        };
        if (!completedEditor)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            DGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DataModel.RefreshDirtyState();
        }, DispatcherPriority.Background);
    }

    private void ViewOption_Changed(object? sender, RoutedEventArgs e)
    {
        // View checkboxes only change which columns are visible. Clicking one
        // can end a DataGrid editor while its provisional typing-dirty flag is
        // still set, so reconcile against the serialized command bytes after
        // Avalonia finishes the column-layout update.
        Dispatcher.UIThread.Post(() =>
        {
            DGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DataModel.RefreshDirtyState();
        }, DispatcherPriority.Background);
    }

    public bool HasPendingChanges => DataModel.IsDirty;
    public bool CanUndo => DataModel.CanUndo;
    public bool CanRedo => DataModel.CanRedo;
    public bool CanUndoAll => DataModel.CanUndoAll;
    public void Undo() => RunHistoryAction(DataModel.Undo);
    public void Redo() => RunHistoryAction(DataModel.Redo);
    public void UndoAll() => RunHistoryAction(DataModel.UndoAll);
    public void Save() => DataModel.Save();
    public void SaveToMaster(string masterPath, string metadataPath) => DataModel.SaveToMaster(masterPath);
    private async void Button_SaveAs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner)
            await owner.SaveAsActiveProjectAsync((master, _) =>
            {
                DataModel.SaveToMaster(master);
                return System.Threading.Tasks.Task.CompletedTask;
            });
    }
    private void Button_LoadIngame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LoadInGame();
    }

    private void Button_Undo(object? sender, RoutedEventArgs e) => Undo();
    private void Button_Redo(object? sender, RoutedEventArgs e) => Redo();
    private void Button_UndoAll(object? sender, RoutedEventArgs e) => UndoAll();

    private void RunHistoryAction(Action restore)
    {
        // A footer click can arrive before DataGrid's queued CellEditEnded dirty
        // refresh. Commit and snapshot that edit synchronously so Undo sees the
        // actual latest state.
        DGrid.CommitEdit(DataGridEditingUnit.Row, true);
        DataModel.RefreshDirtyState();

        // History restoration now replaces only affected row objects inside
        // the existing collections. Never detach ItemsSource or DataContext:
        // doing so while column groups are hidden can invalidate Avalonia's
        // active DataGrid layout pass.
        restore();
    }

    private void Button_CloneAsNewCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DGrid.SelectedItem is not KernelCommands_Wrapper selected)
            {
                DataModel.RecoveryStatus = "Click a command to highlight it. Then click Clone Command.";
                return;
            }

            KernelCommands_Wrapper clone = DataModel.CloneAsNewCommand(selected);
            DGrid.SelectedItem = clone;
            DGrid.ScrollIntoView(clone, null);
        }
        catch (Exception ex)
        {
            DataModel.RecoveryStatus = "ERROR: " + ex.Message;
        }
    }

    private void Button_DeleteClone(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DGrid.SelectedItem is not KernelCommands_Wrapper selected)
            {
                DataModel.RecoveryStatus = "Click a cloned command to highlight it. Only the newest clone can be deleted, and original game commands cannot be removed.";
                return;
            }

            KernelCommands_Wrapper? nextSelection = DataModel.DeleteClonedCommand(selected);
            if (nextSelection != null)
            {
                DGrid.SelectedItem = nextSelection;
                DGrid.ScrollIntoView(nextSelection, null);
            }
        }
        catch (Exception ex)
        {
            DataModel.RecoveryStatus = "ERROR: " + ex.Message;
        }
    }

    private void Filter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        DataModel.ApplyFilter();
    }

    public async Task RestoreOriginalAsync(Window owner)
    {
        try
        {
            if (!await RecoverySource_Util.EnsureConfiguredAsync(owner))
            { DataModel.RecoveryStatus = "Restore Original was cancelled."; return; }
        }
        catch (Exception ex) { DataModel.RecoveryStatus = "ERROR: " + ex.Message; return; }

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
            "will be discarded." + VanillaReference_Service.BuildRestoreTrustNotice(
                [VanillaReference_Service.VerifyProjectFile(projectPath)]);
        RecoveryFileVerification verification = VanillaReference_Service.VerifyProjectFile(projectPath);
        bool confirmed = await AiRevertConfirmationWindow.Show(owner, $"Restore Original {editorName}",
            explanation, originalPath, verification.RequiresWarning ? "Restore Unverified File" : "Restore and Save",
            "Confirming will immediately validate and write the original file into the active project.");
        if (!confirmed)
        {
            DataModel.RecoveryStatus = "Restore Original was cancelled.";
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
            DataModel.RecoveryStatus = "ERROR: " + ex.Message;
        }
    }
}
