using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Modules.BattleFormationEditor;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MixEditor;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Modules.SphereGridEditor;
using FFXProjectEditor.Modules.TreasureMapEditor;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;

namespace FFXProjectEditor;

public partial class Main_Window : Window
{
    private bool _applyingWindowSizePreset;
    private bool _windowHasOpened;
    private Func<Task<Control>>? _activeProjectEditorFactory;
    private string? _activeProjectEditorName;
    private bool _projectTransitionInProgress;
    private bool _closeApproved;
    private bool _closePromptInProgress;
    private readonly Dictionary<IProjectEditorSave, Guid?> _editorProjectBindings = new();
    private readonly object? _welcomeContent;
    Main_DataModel DataModel;
    public Main_Window()
    {
        DataModel = new Main_DataModel();
        this.DataContext = DataModel;
        InitializeComponent();
        _welcomeContent = ContentFrame.Content;
        (double startupWidth, double startupHeight) = LoadSavedWindowSize();
        Width = startupWidth;
        Height = startupHeight;
        SizeChanged += MainWindow_SizeChanged;
        Closing += MainWindow_Closing;
        AddHandler(KeyDownEvent, MainWindow_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            SetWindowSizePresetCheck(startupWidth, startupHeight);
            _windowHasOpened = true;
        }, DispatcherPriority.Render);
        AddHandler(DragDrop.DropEvent, Drop_ProjectFolder);
        LoadRecentProjects();
		RefreshVanillaMasterStatus();
    }

	private async void MenuItem_SetVanillaMaster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		List<string> results = await AvaloniaDialog_Util.OpenFolderDialog(this,
			"Select your clean, unedited FFX Original Game Files folder");
		if (results.Count == 0) return;

		try
		{
			VanillaReference_Service.ValidationResult validation =
				VanillaReference_Service.Validate(results[0], true);
			if (!validation.CanConfigure) throw new InvalidOperationException(validation.Summary);
			VanillaReference_Service.Configure(results[0]);
			RefreshVanillaMasterStatus();
			await RecoveryNotice_Window.Show(this,
				validation.Classification,
				validation.Summary + "\n\nThe folder is now available for recovery and will be treated as read-only.",
				VanillaReference_Service.MasterPath,
				true);
		}
		catch (Exception ex)
		{
			RefreshVanillaMasterStatus();
			await RecoveryNotice_Window.Show(this,
				"Invalid Original Game Files",
				ex.Message + "\n\nSelect a clean folder named master that contains the jppc and new_uspc base folders.",
				results[0],
				false);
		}
	}

    private void MenuItem_FileOpened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshSaveCommandState();
    }

    private async void MenuItem_SaveProject(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveActiveEditorAsync();
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if ((e.Key == Key.Z || e.Key == Key.Y) && ShouldPreserveNativeTextHistory())
            return;
        if (e.Key == Key.Z && ContentFrame.Content is IProjectEditorHistory { CanUndo: true } undoEditor)
        {
            e.Handled = true;
            undoEditor.Undo();
            return;
        }
        if (e.Key == Key.Y && ContentFrame.Content is IProjectEditorHistory { CanRedo: true } redoEditor)
        {
            e.Handled = true;
            redoEditor.Redo();
            return;
        }
        if (e.Key != Key.S) return;
        e.Handled = true;
        if (_projectTransitionInProgress)
        {
            ShowProjectLoadStatus("Save As is still completing. Saving is temporarily unavailable.", false);
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            MenuItem_SaveProjectAs(null, new Avalonia.Interactivity.RoutedEventArgs());
            return;
        }
        await SaveActiveEditorAsync();
    }

    private bool ShouldPreserveNativeTextHistory() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox textBox &&
        !string.Equals(textBox.Tag?.ToString(), "EditorHistoryShortcuts",
            StringComparison.Ordinal);

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved)
        {
            SaveWindowSize();
            return;
        }
        if (_projectTransitionInProgress)
        {
            e.Cancel = true;
            if (_closePromptInProgress) return;
            _closePromptInProgress = true;
            try
            {
                await RecoveryNotice_Window.Show(this, "Save As is still completing",
                    "Zanarkand Workshop must remain open until the new project is completely saved or safely rolled back. Please wait for Save As to finish.",
                    Project_Service.Instance.ProjectPath ?? "", false);
            }
            finally { _closePromptInProgress = false; }
            return;
        }
        if (ContentFrame.Content is not IProjectEditorSave { HasPendingChanges: true })
        {
            SaveWindowSize();
            return;
        }

        e.Cancel = true;
        if (_closePromptInProgress) return;
        _closePromptInProgress = true;
        try
        {
            if (!await ResolvePendingChangesAsync("Closing Zanarkand Workshop will discard them.")) return;
            _closeApproved = true;
            Close();
        }
        finally { _closePromptInProgress = false; }
    }

    internal async Task<bool> SaveActiveEditorAsync()
    {
        if (_projectTransitionInProgress)
        {
            ShowProjectLoadStatus("Save As is still completing. Saving is temporarily unavailable.", false);
            return false;
        }
        if (ContentFrame.Content is not IProjectEditorSave editor || !editor.HasPendingChanges)
            return false;
        if (!await EnsureActiveProjectRegisteredAsync()) return false;
        try
        {
            editor.Save();
            ShowProjectLoadStatus($"Saved {Project_Service.Instance.ActiveProject?.Name ?? "project"}.", true);
            RefreshSaveCommandState();
            return true;
        }
        catch (Exception ex)
        {
            await RecoveryNotice_Window.Show(this, "Project could not be saved", ex.Message,
                Project_Service.Instance.ProjectPath, false);
            RefreshSaveCommandState();
            return false;
        }
    }

    internal async Task<bool> ResolvePendingChangesAsync(string context)
    {
        if (ContentFrame.Content is not IProjectEditorSave { HasPendingChanges: true }) return true;
        PendingChangesDecision decision = await PendingChanges_Window.Show(this, context);
        return decision switch
        {
            PendingChangesDecision.Discard => true,
            PendingChangesDecision.Save => await SaveActiveEditorAsync(),
            _ => false
        };
    }

    internal void RefreshSaveCommandState()
    {
        bool loaded = Project_Service.Instance.IsProjectLoaded;
        SaveProjectMenuItem.IsEnabled = !_projectTransitionInProgress && loaded &&
            ContentFrame.Content is IProjectEditorSave { HasPendingChanges: true };
        SaveAsProjectMenuItem.IsEnabled = !_projectTransitionInProgress && loaded;
    }

    private async void MenuItem_SaveProjectAs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded) return;
        IProjectEditorSave? editor = ContentFrame.Content as IProjectEditorSave;
        await SaveAsActiveProjectAsync((masterPath, metadataPath) =>
        {
            if (editor?.HasPendingChanges == true)
                editor.SaveToMaster(masterPath, metadataPath);
            return Task.CompletedTask;
        });
    }

	private async void MenuItem_ValidateVanillaMaster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		ShowLoading("Verifying Recovery Files", "Hashing only the files currently used by Recovery…");
		VanillaReference_Service.ValidationResult validation;
		try
		{
			validation = await Task.Run(() =>
				VanillaReference_Service.VerifyRecoveryFiles());
		}
		finally { HideLoading(); }
		await RecoveryVerification_Window.ShowResults(this, validation,
			VanillaReference_Service.MasterPath ?? "Not configured");
		RefreshVanillaMasterStatus();
	}

	private async void MenuItem_RestoreCurrentEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (ContentFrame.Content is AutoAbilityEditor_Control autoAbilityEditor)
		{
			await autoAbilityEditor.RestoreOriginalAsync(this);
			return;
		}
		if (ContentFrame.Content is MixEditor_Control mixEditor)
		{
			await mixEditor.RestoreOriginalAsync(this);
			return;
		}
		if (ContentFrame.Content is KernelCommands_Control kernelEditor)
		{
			await kernelEditor.RestoreOriginalAsync(this);
			return;
		}
		if (ContentFrame.Content is SphereGridEditor_Control sphereGridEditor)
		{
			await sphereGridEditor.RestoreOriginalAsync(this);
			return;
		}
		if (ContentFrame.Content is MonEditorSelector_Control monsterSelector)
		{
			if (monsterSelector.ActiveMonsterEditor is MonEditor_Control monsterEditor)
			{
				await monsterEditor.RestoreOriginalAsync(this);
			}
		}
	}

	private async void MenuItem_RestoreEntireMonster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (ContentFrame.Content is MonEditorSelector_Control { ActiveMonsterEditor: { } monsterEditor })
			await monsterEditor.RestoreEntireOriginalAsync(this);
	}

	private async void MenuItem_RestoreFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (!Project_Service.Instance.IsProjectLoaded)
		{
			await RecoveryNotice_Window.Show(this, "No Editing Project",
				"Load an editing project before restoring an individual folder.", null, false);
			return;
		}

		if (ContentFrame.Content is not BattleFormationEditor_Control battleEditor ||
			string.IsNullOrWhiteSpace(battleEditor.SelectedFolderPath))
		{
			await RecoveryNotice_Window.Show(this, "No Battle Folder Selected",
				"Open a formation in the Battle Formation Editor before restoring its folder.",
				null, false);
			return;
		}
		string projectFolder = battleEditor.SelectedFolderPath;

		try
		{
			VanillaReference_Service.FolderRestorePreview preview =
				VanillaReference_Service.PreviewFolderRestore(projectFolder);
			string explanation =
				$"Restore {preview.FileCount:N0} source file(s) in project folder “{preview.RelativeFolder}”. " +
				$"Verified: {preview.VerifiedCount:N0}; unrecognized: {preview.UnrecognizedCount:N0}; " +
				$"not in manifest: {preview.NotInManifestCount:N0}. " +
				"Files that exist only in the editing project will not be removed." +
				VanillaReference_Service.BuildRestoreTrustNotice(preview.Files);
			bool hasWarning = preview.Files.Any(file => file.RequiresWarning);
			bool confirmed = await AiRevertConfirmationWindow.Show(
				this, "Restore Original Folder", explanation, preview.SourceFolder,
				hasWarning ? "Restore Unverified Folder" : "Restore Folder",
				"This immediately writes files to the active editing project.");
			if (!confirmed) return;

			string[] approvedUnverifiedPaths = preview.Files
				.Where(file => file.RequiresWarning)
				.Select(file => file.RelativePath)
				.ToArray();
			VanillaReference_Service.FolderRestoreResult result =
				VanillaReference_Service.RestoreFolder(projectFolder, approvedUnverifiedPaths);
			battleEditor.ReloadAfterFolderRecovery();
			await RecoveryNotice_Window.Show(this, "Folder Restored",
				$"Restored {result.FilesRestored:N0} original file(s) in {result.RelativeFolder}." +
				Environment.NewLine + "Project-only extra files were left untouched.",
				projectFolder, true);
		}
		catch (Exception ex)
		{
			await RecoveryNotice_Window.Show(this, "Folder Could Not Be Restored",
				ex.Message, projectFolder, false);
		}
	}

	private void MenuItem_RecoveryOpened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		string? monsterSection = (ContentFrame.Content as MonEditorSelector_Control)?
			.ActiveMonsterEditor?.ActiveRecoverySectionName;
		RestoreCurrentEditorMenuItem.Header = monsterSection is null
			? "Restore Original in Current Editor"
			: $"Restore Original {monsterSection}";
		RestoreEntireMonsterMenuItem.IsVisible = monsterSection is not null;
		RestoreCurrentEditorMenuItem.IsEnabled =
			ContentFrame.Content is AutoAbilityEditor_Control ||
			ContentFrame.Content is MixEditor_Control ||
			ContentFrame.Content is KernelCommands_Control ||
			ContentFrame.Content is SphereGridEditor_Control ||
			ContentFrame.Content is MonEditorSelector_Control
			{
				ActiveMonsterEditor: not null
			};
		RestoreCurrentBattleFolderMenuItem.IsEnabled =
			ContentFrame.Content is BattleFormationEditor_Control battleEditor &&
			!string.IsNullOrWhiteSpace(battleEditor.SelectedFolderPath);
	}

	private void RefreshVanillaMasterStatus()
	{
		VanillaReference_Service.ValidationResult? validation =
			VanillaReference_Service.GetCachedValidation();
		if (validation is null)
		{
			VanillaMasterStatusMenuItem.Header = string.IsNullOrWhiteSpace(VanillaReference_Service.MasterPath)
				? "Original Game Files: Not configured"
				: "Original Game Files: Configured — files verified as needed";
			ToolTip.SetTip(VanillaMasterStatusMenuItem, VanillaReference_Service.MasterPath);
			return;
		}
		if (validation.IsValid)
		{
			VanillaMasterStatusMenuItem.Header = "Original Game Files: " + validation.Classification;
			ToolTip.SetTip(VanillaMasterStatusMenuItem, VanillaReference_Service.MasterPath);
		}
		else
		{
			VanillaMasterStatusMenuItem.Header = string.IsNullOrWhiteSpace(VanillaReference_Service.MasterPath)
				? "Original Game Files: Not configured"
				: "Original Game Files: " + validation.Classification;
			ToolTip.SetTip(VanillaMasterStatusMenuItem, validation.Summary);
		}
	}

    public async void Drop_ProjectFolder(object sender, DragEventArgs e)
    {
        List<string> files = e.Data.GetFileNames().ToList();

        if (files.Count == 0)
        {
            Debug.WriteLine("No files found on drop");
            return;
        }

        string filePath = Uri.UnescapeDataString(files[0]);

        if (VanillaReference_Service.IsProtectedVanillaPath(filePath))
        {
            await ShowProtectedVanillaProjectWarning(filePath);
            return;
        }

        if (!Project_Service.IsPathValid(filePath))
        {
            ShowProjectLoadStatus("INVALID: Select the FFX project master folder.", false);
            return;
        }

        await LoadProjectWithOverlay(filePath);
    }

    private async void Button_ProjectPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await PromptForProjectFolderAsync();
    }

    private async void Button_WelcomeProject(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<ProjectManifest> knownProjects = ProjectRegistry_Service.GetProjects()
            .ToList();
        if (knownProjects.Count == 0)
        {
            await PromptForProjectFolderAsync();
            return;
        }

        KnownProjectChoice? choice = await KnownProjects_Window.Show(this, knownProjects);
        if (choice is null) return;
        if (choice.Browse)
        {
            await PromptForProjectFolderAsync();
            return;
        }
        if (!string.IsNullOrWhiteSpace(choice.MasterPath))
            await OpenSelectedProjectAsync(choice.MasterPath);
    }

    private async Task PromptForProjectFolderAsync()
    {
        List<string> openDialogResults = await AvaloniaDialog_Util.OpenFolderDialog(this, "Select the project folder");
        if (openDialogResults.Count == 0 || !Directory.Exists(openDialogResults[0]))
        {
            return;
        }

        await OpenSelectedProjectAsync(openDialogResults[0]);
    }

    private async Task OpenSelectedProjectAsync(string path)
    {

        if (!Project_Service.IsPathValid(path))
        {
            Debug.WriteLine("Selected directory is not a valid master project folder");
            ShowProjectLoadStatus("INVALID: Select the FFX project master folder.", false);
            return;
        }

        if (VanillaReference_Service.IsProtectedVanillaPath(path))
        {
            await ShowProtectedVanillaProjectWarning(path);
            return;
        }

        await LoadProjectWithOverlay(path);
    }

    private void LoadRecentProjects()
    {
        RefreshRecentProjectsMenu();
    }

    private void RememberRecentProject(string path)
    {
        try
        {
            ProjectRegistry_Service.RememberUnregisteredPath(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not save recent projects: {ex.Message}");
        }

        RefreshRecentProjectsMenu();
    }

    private void RefreshRecentProjectsMenu()
    {
        List<ProjectManifest> registered = ProjectRegistry_Service.GetProjects().ToList();
        var items = new List<MenuItem>();
        items.AddRange(registered.Select(project =>
        {
            var item = new MenuItem { Header = project.Name };
            ToolTip.SetTip(item, project.MasterPath);
            item.Click += (_, _) => OpenRecentProject(project.MasterPath);
            return item;
        }));
        IEnumerable<string> unregisteredPaths = ProjectRegistry_Service.Registry.RecentUnregisteredMasterPaths.Where(path =>
            registered.All(project => !string.Equals(project.MasterPath, path, StringComparison.OrdinalIgnoreCase)));
        items.AddRange(unregisteredPaths.Select(path =>
        {
            var item = new MenuItem { Header = path };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => OpenRecentProject(path);
            return item;
        }));
        RecentProjectsMenu.IsEnabled = items.Count > 0;
        RecentProjectsMenu.ItemsSource = items;
    }

    private async void OpenRecentProject(string path)
    {
        if (!Directory.Exists(path) || !Project_Service.IsPathValid(path))
        {
            ShowProjectLoadStatus("INVALID: This recent project is no longer a valid FFX master folder.", false);
            return;
        }

        if (VanillaReference_Service.IsProtectedVanillaPath(path))
        {
            await ShowProtectedVanillaProjectWarning(path);
            return;
        }

        await LoadProjectWithOverlay(path);
    }

    private async Task LoadProjectWithOverlay(string path)
    {
        if (!await ResolvePendingChangesAsync("Opening another project will replace the current editor.")) return;
        ShowLoading("Loading project", "Preparing the selected FFX master folder…");
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            DataModel.LoadProjectFolder(path);
            UnbindCurrentEditor();
            ContentFrame.Content = _welcomeContent;
            _activeProjectEditorFactory = null;
            _activeProjectEditorName = null;
            RefreshSaveCommandState();
            RememberRecentProject(path);
            UpdateActiveProjectDisplay();
            string projectName = Project_Service.Instance.ActiveProject?.Name ??
                Path.GetFileName(Directory.GetParent(path)?.FullName ?? path);
            ShowProjectLoadStatus($"Successfully loaded {projectName}", true);
        }
        catch (Exception ex)
        {
            ShowProjectLoadStatus("FAILED: " + ex.Message, false);
            await RecoveryNotice_Window.Show(this, "Project could not be loaded", ex.Message, path, false);
        }
        finally { HideLoading(); }
    }

    public async Task<bool> EnsureActiveProjectRegisteredAsync()
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return false;
        if (Project_Service.Instance.IsProjectRegistered)
            return ValidateCurrentEditorBinding();

        string masterPath = Project_Service.Instance.ProjectPath!;
        string suggestedName = Path.GetFileName(Directory.GetParent(masterPath)?.FullName ?? masterPath);
        string? name = await ProjectName_Window.Show(this, suggestedName);
        if (name is null) return false;
        try
        {
            ProjectManifest manifest = Project_Service.Instance.RegisterActiveProject(name);
            SphereGridColorMetadata.MigrateLegacyLocation(
                Project_Service.Instance.Path_ZanarkandWorkshopMetadata,
                Project_Service.Instance.Path_PathHashedProjectMetadata);
            BindCurrentEditor(manifest.ProjectId);
            RefreshRecentProjectsMenu();
            UpdateActiveProjectDisplay();
            ShowProjectLoadStatus($"Project '{manifest.Name}' created.", true);
            return true;
        }
        catch (Exception ex)
        {
            await RecoveryNotice_Window.Show(this, "Project could not be created", ex.Message, masterPath, false);
            return false;
        }
    }

    private bool ValidateCurrentEditorBinding()
    {
        if (ContentFrame.Content is not IProjectEditorSave editor) return true;
        Guid activeId = Project_Service.Instance.ActiveProject!.ProjectId;
        if (!_editorProjectBindings.TryGetValue(editor, out Guid? boundId) || boundId is null)
        {
            BindCurrentEditor(activeId);
            return true;
        }
        if (boundId == activeId) return true;
        ShowProjectLoadStatus("BLOCKED: This editor belongs to a different project. Reopen it from the Editors menu.", false);
        return false;
    }

    private void BindCurrentEditor(Guid? projectId = null)
    {
        if (ContentFrame.Content is IProjectEditorSave editor)
            _editorProjectBindings[editor] = projectId ?? Project_Service.Instance.ActiveProject?.ProjectId;
    }

    private void UnbindCurrentEditor()
    {
        if (ContentFrame.Content is IProjectEditorSave editor)
            _editorProjectBindings.Remove(editor);
    }

    public async Task<bool> SaveAsActiveProjectAsync(Func<string, string, Task> writePendingChanges)
    {
        if (_projectTransitionInProgress || !Project_Service.Instance.IsProjectLoaded)
            return false;
        string sourceMaster = Project_Service.Instance.ProjectPath!;
        ProjectManifest? sourceProject = Project_Service.Instance.ActiveProject;
        if (sourceProject is not null && !ValidateCurrentEditorBinding()) return false;
        string suggestedName = sourceProject is null
            ? Path.GetFileName(Directory.GetParent(sourceMaster)?.FullName ?? sourceMaster) + " Copy"
            : sourceProject.Name + " Copy";
        string selectedProjectPath = await AvaloniaDialog_Util.SaveFileDialog(
            this,
            "Choose a Name and Location for the New Project Folder",
            suggestedName,
            fileTypeChoices:
            [
                new Avalonia.Platform.Storage.FilePickerFileType(
                    "Project folder (complete Master copy)")
                {
                    Patterns = ["*"]
                }
            ]);
        if (string.IsNullOrWhiteSpace(selectedProjectPath)) return false;

        string finalProjectDirectory = Path.GetFullPath(selectedProjectPath);
        string? parent = Path.GetDirectoryName(finalProjectDirectory);
        if (string.IsNullOrWhiteSpace(parent))
        {
            await RecoveryNotice_Window.Show(this, "Save As could not start",
                "Select a project name and location.", finalProjectDirectory, false);
            return false;
        }
        string name;
        try { name = ProjectRegistry_Service.ValidateNewName(Path.GetFileName(finalProjectDirectory)); }
        catch (Exception ex)
        {
            await RecoveryNotice_Window.Show(this, "Save As could not start",
                ex.Message, finalProjectDirectory, false);
            return false;
        }
        string finalMaster = Path.Combine(finalProjectDirectory, "master");
        if (IsPathInside(finalProjectDirectory, sourceMaster))
        {
            await RecoveryNotice_Window.Show(this, "Save As could not start",
                "The new project cannot be created inside the Master folder being copied.",
                finalProjectDirectory, false);
            return false;
        }
        if (Directory.Exists(finalProjectDirectory) || File.Exists(finalProjectDirectory))
        {
            await RecoveryNotice_Window.Show(this, "Save As could not start",
                $"A file or folder named '{name}' already exists at the selected destination.",
                finalProjectDirectory, false);
            return false;
        }

        string temporaryProjectDirectory = Path.Combine(parent, $".{name}.zwcopying-{Guid.NewGuid():N}");
        string temporaryMaster = Path.Combine(temporaryProjectDirectory, "master");
        string temporaryMetadata = Path.Combine(ProgramMetadata_Service.RootPath, $".saveas-{Guid.NewGuid():N}");
        _projectTransitionInProgress = true;
        RefreshSaveCommandState();
        bool projectActivated = false;
        bool finalDirectoryCreated = false;
        ProjectManifest? createdProject = null;
        ShowLoading("Saving project as " + name, "Copying the complete Master folder…");
        try
        {
            await Task.Run(() => CopyDirectory(sourceMaster, temporaryMaster));
            if (Directory.Exists(Project_Service.Instance.Path_ZanarkandWorkshopMetadata))
                CopyDirectory(Project_Service.Instance.Path_ZanarkandWorkshopMetadata, temporaryMetadata);
            else
                Directory.CreateDirectory(temporaryMetadata);
            await writePendingChanges(temporaryMaster, temporaryMetadata);
            Directory.Move(temporaryProjectDirectory, finalProjectDirectory);
            finalDirectoryCreated = true;
            ProjectManifest created = ProjectRegistry_Service.Register(
                name, finalMaster, sourceProject?.ProjectId);
            createdProject = created;
            string createdMetadata = Path.Combine(ProjectRegistry_Service.ProjectsRoot, created.Name);
            foreach (string file in Directory.EnumerateFiles(temporaryMetadata, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(file), "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(createdMetadata, Path.GetRelativePath(temporaryMetadata, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
            DataModel.LoadProjectFolder(created.MasterPath);
            projectActivated = true;
            RememberRecentProject(created.MasterPath);
            UpdateActiveProjectDisplay();
            RefreshRecentProjectsMenu();

            if (_activeProjectEditorFactory is not null)
            {
                LoadingDetailText.Text = "Reloading " + (_activeProjectEditorName ?? "editor") + " from the new project…";
                try
                {
                    UnbindCurrentEditor();
                    ContentFrame.Content = await _activeProjectEditorFactory();
                    BindCurrentEditor(created.ProjectId);
                }
                catch (Exception reloadError)
                {
                    UnbindCurrentEditor();
                    ContentFrame.Content = _welcomeContent;
                    await RecoveryNotice_Window.Show(this, "Project saved; editor could not be reopened",
                        "The new project is active and its changes were saved. Open the editor again from the Editors menu.\n\n" +
                        reloadError.Message, created.MasterPath, false);
                }
            }
            ShowProjectLoadStatus($"Saved as '{created.Name}'. This is now the active project.", true);
            return true;
        }
        catch (Exception ex)
        {
            TryDeleteCreatedDirectory(temporaryProjectDirectory);
            var rollbackProblems = new List<string>();
            bool registryRolledBack = createdProject is null;
            if (!projectActivated && createdProject is not null)
            {
                try
                {
                    ProjectRegistry_Service.RollbackNewProject(
                        createdProject.ProjectId, sourceProject?.ProjectId);
                    registryRolledBack = true;
                }
                catch (Exception rollbackError)
                {
                    rollbackProblems.Add("Workshop registration could not be rolled back: " + rollbackError.Message);
                }
            }
            if (!projectActivated && finalDirectoryCreated && registryRolledBack)
            {
                try { Directory.Delete(finalProjectDirectory, true); }
                catch (Exception rollbackError)
                {
                    rollbackProblems.Add("The new project folder could not be removed: " + rollbackError.Message);
                }
            }
            if (!projectActivated && !string.Equals(Project_Service.Instance.ProjectPath, sourceMaster,
                    StringComparison.OrdinalIgnoreCase))
            {
                try { DataModel.LoadProjectFolder(sourceMaster); }
                catch (Exception rollbackError)
                {
                    rollbackProblems.Add("The original project could not be restored in the active session: " + rollbackError.Message);
                }
            }
            string recoveryDetails = rollbackProblems.Count == 0
                ? ""
                : "\n\nRecovery attention is required:\n" + string.Join("\n", rollbackProblems) +
                  $"\nThe new copy has been preserved at:\n{finalProjectDirectory}";
            await RecoveryNotice_Window.Show(this, "Save As failed",
                ex.Message + (projectActivated
                    ? "\n\nThe new project was created and remains active."
                    : "\n\nThe original project remains active and unchanged.") + recoveryDetails,
                finalProjectDirectory, false);
            return false;
        }
        finally
        {
            _projectTransitionInProgress = false;
            RefreshSaveCommandState();
            TryDeleteCreatedDirectory(temporaryMetadata);
            HideLoading();
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }

    private static bool IsPathInside(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)));
        return relative == "." ||
            (!Path.IsPathRooted(relative) && relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void TryDeleteCreatedDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    private void UpdateActiveProjectDisplay()
    {
        ProjectManifest? project = Project_Service.Instance.ActiveProject;
        Title = project is null ? "Unregistered Project — Zanarkand Workshop" :
            $"{project.Name} — Zanarkand Workshop";
    }

    private async System.Threading.Tasks.Task ShowProtectedVanillaProjectWarning(string path)
    {
        const string message = "This folder contains your verified Original Game Files and is protected from editing. " +
            "Select a separate project Master folder to modify. The original files remain available through Recovery and Restore Original.";
        ShowProjectLoadStatus("BLOCKED: The protected Original Game Files folder cannot be opened for editing.", false);
        await RecoveryNotice_Window.Show(this, "Protected Original Game Files", message,
            VanillaReference_Service.NormalizeMasterPath(path), false);
    }

    private void ShowProjectLoadStatus(string message, bool success)
    {
        ProjectLoadStatusText.Text = message;
        ProjectLoadStatusText.Foreground = success ? Brushes.LimeGreen : Brushes.Red;
    }

    private void MenuItem_ClearProjectLoadStatus(object? sender, PointerPressedEventArgs e)
    {
        ProjectLoadStatusText.Text = string.Empty;
    }

    private void MenuItem_Exit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void MenuItem_WindowSize1024(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetWindowSize(1024, 640);
    }

    private void MenuItem_WindowSize1280(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetWindowSize(1280, 720);
    }

    private void MenuItem_WindowSize1600(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetWindowSize(1600, 900);
    }

    private void MenuItem_WindowSize1920(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetWindowSize(1920, 1080);
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_windowHasOpened || _applyingWindowSizePreset) return;
        ClearWindowSizePresetChecks();
    }

    private void ClearWindowSizePresetChecks()
    {
        WindowSize1280MenuItem.IsChecked = false;
        WindowSize1600MenuItem.IsChecked = false;
        WindowSize1920MenuItem.IsChecked = false;
    }

    private void SetWindowSizePresetCheck(double width, double height)
    {
        WindowSize1280MenuItem.IsChecked = width == 1280 && height == 720;
        WindowSize1600MenuItem.IsChecked = width == 1600 && height == 900;
        WindowSize1920MenuItem.IsChecked = width == 1920 && height == 1080;
    }

    private (double Width, double Height) LoadSavedWindowSize()
    {
        try
        {
            WindowSettings settings = AppSettings_Service.Current.Window;
            if (settings.Width >= MinWidth && settings.Height >= MinHeight)
                return (settings.Width, settings.Height);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not restore window size: {ex.Message}");
        }
        return (1280, 720);
    }

    private void SaveWindowSize()
    {
        try
        {
            double width = Math.Max(MinWidth, Bounds.Width);
            double height = Math.Max(MinHeight, Bounds.Height);
            AppSettings_Service.Current.Window.Width = width;
            AppSettings_Service.Current.Window.Height = height;
            AppSettings_Service.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not save window size: {ex.Message}");
        }
    }

    private void SetWindowSize(double width, double height)
    {
        _applyingWindowSizePreset = true;
        WindowState = WindowState.Normal;
        Dispatcher.UIThread.Post(() =>
        {
            Width = width;
            Height = height;
            SetWindowSizePresetCheck(width, height);

			// A preset can be selected immediately after the window was manually
			// compressed. Refresh every active descendant so wrapped toolbars and
			// splitter-backed grids do not retain their minimum-size measurements.
			Dispatcher.UIThread.Post(() =>
			{
				RefreshActiveEditorLayout();
				_applyingWindowSizePreset = false;
			}, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

	private void RefreshActiveEditorLayout()
	{
		foreach (Control control in ContentFrame.GetVisualDescendants().OfType<Control>())
		{
			control.InvalidateMeasure();
			control.InvalidateArrange();
		}

		ContentFrame.InvalidateMeasure();
		ContentFrame.InvalidateArrange();
		InvalidateMeasure();
		InvalidateArrange();
	}

    private async void Button_EditorHelp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await new EditorHelp_Window().ShowDialog(this);
    }

    private async void MenuItem_MonsterEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

		await OpenProjectEditor("Monster Editor", Project_Service.Instance.Path_Mon, true,
			() => new MonEditorSelector_Control());
    }
    private async void MenuItem_Commands(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

		await OpenProjectEditor("Player & Aeon Commands", Project_Service.Instance.Path_KernelCommandUs, false,
			() => new KernelCommands_Control(CommandFile_enum.Command));
    }
    private async void MenuItem_AutoAbilities(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        string abilityPath = Project_Service.Instance.Path_KernelAutoAbilityUs;
        string recipePath = Project_Service.Instance.Path_KernelCustomization;
        if (!File.Exists(abilityPath) && !File.Exists(recipePath))
        {
            await RecoveryNotice_Window.Show(this, "Auto Abilities is unavailable",
                "The editor requires at least one of its data files, but both are missing.\n\n" +
                "Properties & Effects:\n" + abilityPath + "\n\n" +
                "Recipes:\n" + recipePath,
                Project_Service.Instance.ProjectPath, false);
            return;
        }
        await OpenProjectEditor("Auto Ability Editor",
            File.Exists(abilityPath) ? abilityPath : recipePath, false,
            () => new AutoAbilityEditor_Control());
    }
    private async void MenuItem_Items(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

		await OpenProjectEditor("Items", Project_Service.Instance.Path_KernelItemUs, false,
			() => new KernelCommands_Control(CommandFile_enum.Item));
    }
    private async void MenuItem_MixRecipes(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        string recipePath = Project_Service.Instance.Path_KernelMixRecipes;
        string commandPath = Project_Service.Instance.Path_KernelCommandUs;
        if (!File.Exists(recipePath) || !File.Exists(commandPath))
        {
            await RecoveryNotice_Window.Show(this, "Rikku Mix Recipes is unavailable",
                "This editor requires both prepare.bin and the localized command.bin.\n\n" +
                "Recipes:\n" + recipePath + "\n\n" +
                "Result names:\n" + commandPath,
                Project_Service.Instance.ProjectPath, false);
            return;
        }

        await OpenProjectEditor("Rikku Mix Recipes", recipePath, false,
            () => new MixEditor_Control());
    }
    private async void MenuItem_MonsterMagic1(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

		await OpenProjectEditor("Standard Monster Commands", Project_Service.Instance.Path_KernelMonMagic1Us, false,
			() => new KernelCommands_Control(CommandFile_enum.MonMagic1));
    }
    private async void MenuItem_MonsterMagic2(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

		await OpenProjectEditor("Boss Commands", Project_Service.Instance.Path_KernelMonMagic2Us, false,
			() => new KernelCommands_Control(CommandFile_enum.MonMagic2));
    }

    private async void MenuItem_BattleFormationEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        await OpenProjectEditorAsync(
            "Battle Formation Editor",
            Project_Service.Instance.Path_Btl,
            true,
            async () =>
            {
                BattleFormationEditor_DataModel model = await Task.Run(
                    () => new BattleFormationEditor_DataModel());
                return new BattleFormationEditor_Control(model);
            },
            "Scanning and validating battle formations…");
    }

    private async void MenuItem_SphereGridEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        try
        {
            var mismatches = new List<SphereGridHeaderMismatch>();
            foreach (SphereGridKind kind in Enum.GetValues<SphereGridKind>())
            {
                SphereGridFileSet files = SphereGridFileSet.FromDirectory(
                    Project_Service.Instance.Path_SphereGrid, kind);
                SphereGridHeaderMismatch? mismatch = SphereGridHeaderRepair.Inspect(files);
                if (mismatch is not null)
                    mismatches.Add(mismatch);
            }

            if (mismatches.Count > 0)
            {
                SphereGridHeaderMismatch? unsafeMismatch =
                    mismatches.FirstOrDefault(mismatch => !mismatch.CanRepair);
                if (unsafeMismatch is not null)
                {
                    await RecoveryNotice_Window.Show(
                        this,
                        "Sphere Grid Could Not Be Repaired",
                        unsafeMismatch.Description + Environment.NewLine + Environment.NewLine +
                        "The available node-type data does not exactly match the layout count. " +
                        "Use Recovery to restore the original grids.",
                        unsafeMismatch.Files.ContentPath,
                        false);
                    return;
                }

                string explanation =
                    "The Sphere Grid files contain conflicting node counts:" +
                    Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, mismatches.Select(mismatch => mismatch.Description)) +
                    Environment.NewLine + Environment.NewLine +
                    "Complete node-type data is present, so the content header can be corrected safely.";
                string sources = string.Join(
                    Environment.NewLine,
                    mismatches.Select(mismatch => mismatch.Files.ContentPath));
                bool repair = await AiRevertConfirmationWindow.Show(
                    this,
                    "Sphere Grid Repair Required",
                    explanation,
                    sources,
                    "Repair and Open",
                    "Use Recovery if you need to restore the game's original Sphere Grid files.");
                if (!repair)
                    return;

                foreach (SphereGridHeaderMismatch mismatch in mismatches)
                    SphereGridHeaderRepair.Repair(mismatch);
            }
        }
        catch (Exception ex)
        {
            await RecoveryNotice_Window.Show(
                this,
                "Sphere Grid Repair Failed",
                ex.Message,
                Project_Service.Instance.Path_SphereGrid,
                false);
            return;
        }

        await OpenProjectEditor("Sphere Grid Editor", Project_Service.Instance.Path_SphereGrid, true,
            () => new SphereGridEditor_Control());
    }

    private async void MenuItem_TreasureMapEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded) return;
        string required = Path.Combine(Project_Service.Instance.ProjectPath!, "jppc", "map");
        await OpenProjectEditorAsync("Treasure Map Editor", required, true, async () =>
        {
            TreasureMapEditor_DataModel model = await Task.Run(() => new TreasureMapEditor_DataModel(message =>
                Dispatcher.UIThread.Post(() => LoadingDetailText.Text = message)));
            return new TreasureMapEditor_Control(model);
        }, "Scanning map geometry, event scripts, and treasure locations…");
    }

	private async Task OpenProjectEditorAsync(
		string editorName, string requiredPath, bool requiredIsDirectory,
		Func<Task<Control>> createEditor, string detail)
	{
		if (!await ResolvePendingChangesAsync("Opening another editor will replace the current editor.")) return;
		bool exists = requiredIsDirectory ? Directory.Exists(requiredPath) : File.Exists(requiredPath);
		if (!exists)
		{
			await RecoveryNotice_Window.Show(this, editorName + " is unavailable",
				requiredIsDirectory ? "This editor couldn’t be opened because a required folder is missing." : "This editor couldn’t be opened because a required file is missing.",
				requiredPath, false);
			return;
		}
		ShowLoading("Opening " + editorName, detail);
		try
		{
			await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
			UnbindCurrentEditor();
			ContentFrame.Content = await createEditor();
			BindCurrentEditor();
			_activeProjectEditorFactory = createEditor;
			_activeProjectEditorName = editorName;
			RefreshSaveCommandState();
		}
		catch (Exception ex)
		{
			await RecoveryNotice_Window.Show(this, editorName + " could not be opened",
				"The required data exists, but the editor could not read it.\n\n" + ex.Message, requiredPath, false);
		}
		finally { HideLoading(); }
	}

	private void ShowLoading(string title, string detail)
	{
		LoadingTitleText.Text = title;
		LoadingDetailText.Text = detail;
		LoadingOverlay.IsVisible = true;
	}

	private void HideLoading() => LoadingOverlay.IsVisible = false;

	private async System.Threading.Tasks.Task OpenProjectEditor(
		string editorName, string requiredPath, bool requiredIsDirectory, Func<Control> createEditor)
	{
		if (!await ResolvePendingChangesAsync("Opening another editor will replace the current editor.")) return;
		bool exists = requiredIsDirectory ? Directory.Exists(requiredPath) : File.Exists(requiredPath);
		if (!exists)
		{
			await RecoveryNotice_Window.Show(this, editorName + " is unavailable",
				requiredIsDirectory
					? "This editor couldn’t be opened because a required folder is missing. Close this message to continue using the program."
					: "This editor couldn’t be opened because a required file is missing. Close this message to continue using the program.",
				requiredPath, false);
			return;
		}

		try
		{
			ShowLoading("Opening " + editorName, "Reading and validating project data…");
			await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
			UnbindCurrentEditor();
			ContentFrame.Content = createEditor();
			BindCurrentEditor();
			_activeProjectEditorFactory = () => Task.FromResult(createEditor());
			_activeProjectEditorName = editorName;
			RefreshSaveCommandState();
		}
		catch (Exception ex)
		{
			await RecoveryNotice_Window.Show(this, editorName + " could not be opened",
				"The required data exists, but the editor could not read it.\n\n" + ex.Message,
				requiredPath, false);
		}
		finally { HideLoading(); }
	}

    private async Task OpenUtilityAsync(Func<Control> createUtility)
    {
        if (!await ResolvePendingChangesAsync("Opening a utility will replace the current editor.")) return;
        UnbindCurrentEditor();
        ContentFrame.Content = createUtility();
        _activeProjectEditorFactory = null;
        _activeProjectEditorName = null;
        RefreshSaveCommandState();
    }
    private async void MenuItem_DebugMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await OpenUtilityAsync(() => new DebugMenu_Control());
    private async void MenuItem_BattleTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await OpenUtilityAsync(() => new BattleTracker_Control());
    private async void MenuItem_InventoryTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await OpenUtilityAsync(() => new InventoryTracker_Control());
    private async void MenuItem_ArenaTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await OpenUtilityAsync(() => new ArenaTracker_Control());
    private async void MenuItem_Test(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await OpenUtilityAsync(() => new Test_Control());
}
