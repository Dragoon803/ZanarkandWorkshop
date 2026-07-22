using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;

namespace FFXProjectEditor;

public partial class Main_Window : Window
{
    private const int MaxRecentProjects = 5;
    private readonly string _recentProjectsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "recent-projects.txt");
    private readonly string _windowSizePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "window-size.txt");
    private readonly List<string> _recentProjects = new();
    private bool _applyingWindowSizePreset;
    private bool _windowHasOpened;
    Main_DataModel DataModel;
    public Main_Window()
    {
        DataModel = new Main_DataModel();
        this.DataContext = DataModel;
        InitializeComponent();
        (double startupWidth, double startupHeight) = LoadSavedWindowSize();
        Width = startupWidth;
        Height = startupHeight;
        SizeChanged += MainWindow_SizeChanged;
        Closing += (_, _) => SaveWindowSize();
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
			VanillaReference_Service.ValidationResult validation = VanillaReference_Service.Validate(results[0]);
			if (!validation.IsValid) throw new InvalidOperationException(validation.Summary);
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
				ex.Message + "\n\nSelect a clean folder named master that contains both jppc\\battle\\mon and new_uspc\\battle\\kernel.",
				results[0],
				false);
		}
	}

	private async void MenuItem_ValidateVanillaMaster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		VanillaReference_Service.ValidationResult validation =
			VanillaReference_Service.Validate(VanillaReference_Service.MasterPath);
		await RecoveryNotice_Window.Show(this,
			validation.Classification,
			validation.Summary,
			VanillaReference_Service.MasterPath,
			validation.IsValid);
		RefreshVanillaMasterStatus();
	}

	private void RefreshVanillaMasterStatus()
	{
		VanillaReference_Service.ValidationResult validation =
			VanillaReference_Service.Validate(VanillaReference_Service.MasterPath);
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

        DataModel.LoadProjectFolder(filePath);
        RememberRecentProject(filePath);
        ShowProjectLoadStatus("SUCCESS: Master folder loaded successfully.", true);
    }

    private async void Button_ProjectPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<string> openDialogResults = await AvaloniaDialog_Util.OpenFolderDialog(this, "Select the project folder");
        if (openDialogResults.Count == 0 || !Directory.Exists(openDialogResults[0]))
        {
            return;
        }

        if (!Project_Service.IsPathValid(openDialogResults[0]))
        {
            Debug.WriteLine("Selected directory is not a valid master project folder");
            ShowProjectLoadStatus("INVALID: Select the FFX project master folder.", false);
            return;
        }

        if (VanillaReference_Service.IsProtectedVanillaPath(openDialogResults[0]))
        {
            await ShowProtectedVanillaProjectWarning(openDialogResults[0]);
            return;
        }

        DataModel.LoadProjectFolder(openDialogResults[0]);
        RememberRecentProject(openDialogResults[0]);
        ShowProjectLoadStatus("SUCCESS: Master folder loaded successfully.", true);
    }

    private void LoadRecentProjects()
    {
        try
        {
            if (File.Exists(_recentProjectsPath))
            {
                _recentProjects.AddRange(File.ReadAllLines(_recentProjectsPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxRecentProjects));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read recent projects: {ex.Message}");
        }

        RefreshRecentProjectsMenu();
    }

    private void RememberRecentProject(string path)
    {
        string normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _recentProjects.RemoveAll(existing =>
            string.Equals(existing, normalizedPath, StringComparison.OrdinalIgnoreCase));
        _recentProjects.Insert(0, normalizedPath);
        if (_recentProjects.Count > MaxRecentProjects)
            _recentProjects.RemoveRange(MaxRecentProjects, _recentProjects.Count - MaxRecentProjects);

        try
        {
            string? directory = Path.GetDirectoryName(_recentProjectsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(_recentProjectsPath, _recentProjects);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not save recent projects: {ex.Message}");
        }

        RefreshRecentProjectsMenu();
    }

    private void RefreshRecentProjectsMenu()
    {
        RecentProjectsMenu.IsEnabled = _recentProjects.Count > 0;
        RecentProjectsMenu.ItemsSource = _recentProjects.Select(path =>
        {
            var item = new MenuItem { Header = path };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => OpenRecentProject(path);
            return item;
        }).ToList();
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

        DataModel.LoadProjectFolder(path);
        RememberRecentProject(path);
        ShowProjectLoadStatus("SUCCESS: Master folder loaded successfully.", true);
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
            if (!File.Exists(_windowSizePath)) return (1280, 720);
            string[] values = File.ReadAllText(_windowSizePath).Split('x', StringSplitOptions.TrimEntries);
            if (values.Length == 2 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height) &&
                width >= MinWidth && height >= MinHeight)
                return (width, height);
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
            string? directory = Path.GetDirectoryName(_windowSizePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            double width = Math.Max(MinWidth, Bounds.Width);
            double height = Math.Max(MinHeight, Bounds.Height);
            File.WriteAllText(_windowSizePath,
                $"{width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}");
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
    private async void MenuItem_Items(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

		await OpenProjectEditor("Items", Project_Service.Instance.Path_KernelItemUs, false,
			() => new KernelCommands_Control(CommandFile_enum.Item));
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

	private async System.Threading.Tasks.Task OpenProjectEditor(
		string editorName, string requiredPath, bool requiredIsDirectory, Func<Control> createEditor)
	{
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
			ContentFrame.Content = createEditor();
		}
		catch (Exception ex)
		{
			await RecoveryNotice_Window.Show(this, editorName + " could not be opened",
				"The required data exists, but the editor could not read it.\n\n" + ex.Message,
				requiredPath, false);
		}
	}
    private void MenuItem_DebugMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ContentFrame.Content = new DebugMenu_Control();
    }
    private void MenuItem_BattleTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ContentFrame.Content = new BattleTracker_Control();
    }
    private void MenuItem_InventoryTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ContentFrame.Content = new InventoryTracker_Control();
    }
    private void MenuItem_ArenaTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ContentFrame.Content = new ArenaTracker_Control();
    }


    private void MenuItem_Test(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ContentFrame.Content = new Test_Control();
    }
}
