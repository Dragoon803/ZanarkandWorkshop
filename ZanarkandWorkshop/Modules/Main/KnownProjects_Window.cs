using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FFXProjectEditor.Services;
using FFXProjectEditor.Modules.MonEditor;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor;

internal sealed record KnownProjectChoice(string? MasterPath, bool Browse);

internal sealed class KnownProjects_Window : Window
{
    private sealed record ProjectItem(ProjectManifest Manifest)
    {
        public string Name => Manifest.Name;
        public string Path => Manifest.MasterPath;
        public bool Available => Project_Service.IsPathValid(Manifest.MasterPath);
        public string Status => Available ? "Ready" : "Master folder missing — relink or remove this entry";
    }

    private readonly ListBox _projects;
    private readonly List<ProjectManifest> _manifests;
    private readonly Button _open;
    private readonly Button _relink;
    private readonly Button _remove;

    private KnownProjects_Window(IReadOnlyList<ProjectManifest> projects)
    {
        _manifests = projects.ToList();
        Title = "Open Project";
        Width = 650;
        Height = 440;
        MinWidth = 520;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _projects = new ListBox
        {
            ItemsSource = MakeItems(),
            ItemTemplate = new FuncDataTemplate<ProjectItem>((item, _) => new StackPanel
            {
                Margin = new Thickness(6, 4),
                Children =
                {
                    new TextBlock { Text = item?.Name, FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = item?.Path, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = item?.Status,
                        Foreground = item?.Available == true ? Brushes.LightGreen : Brushes.Orange,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            })
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var browse = new Button { Content = "Browse for Master…", MinWidth = 140 };
        _relink = new Button { Content = "Relink…", MinWidth = 85, IsEnabled = false };
        _remove = new Button { Content = "Forget Project", MinWidth = 125, IsEnabled = false };
        _open = new Button { Content = "Open Project", MinWidth = 110, IsDefault = true, IsEnabled = false };
        cancel.Click += (_, _) => Close(null);
        browse.Click += (_, _) => Close(new KnownProjectChoice(null, true));
        _open.Click += (_, _) => OpenSelection();
        _relink.Click += async (_, _) => await RelinkSelectionAsync();
        _remove.Click += async (_, _) => await RemoveSelectionAsync();
        _projects.SelectionChanged += (_, _) => RefreshActions();
        _projects.DoubleTapped += (_, _) => OpenSelection();

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                Children =
                {
                    new TextBlock { Text = "Choose a project", FontSize = 24, FontWeight = FontWeight.Bold },
                    Place(new TextBlock
                    {
                        Text = "Open a known project or browse for another FFX Master folder.",
                        Foreground = Brushes.LightGray,
                        Margin = new Thickness(0, 5, 0, 14)
                    }, 1),
                    Place(_projects, 2),
                    Place(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 14, 0, 0),
                        Children = { _relink, _remove, browse, cancel, _open }
                    }, 3)
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(null);
            else if (e.Key == Key.Enter) OpenSelection();
        };
        Opened += (_, _) =>
        {
            _projects.SelectedIndex = 0;
            _projects.Focus();
        };
    }

    private void OpenSelection()
    {
        if (_projects.SelectedItem is ProjectItem { Available: true } item)
            Close(new KnownProjectChoice(item.Path, false));
    }

    private List<ProjectItem> MakeItems() => _manifests
        .OrderByDescending(project => project.LastOpenedUtc)
        .Select(project => new ProjectItem(project)).ToList();

    private void RefreshActions()
    {
        bool selected = _projects.SelectedItem is ProjectItem;
        bool active = _projects.SelectedItem is ProjectItem item &&
            Project_Service.Instance.ActiveProject?.ProjectId == item.Manifest.ProjectId;
        _open.IsEnabled = _projects.SelectedItem is ProjectItem { Available: true };
        _relink.IsEnabled = selected && !active;
        _remove.IsEnabled = selected && !active;
        ToolTip.SetTip(_relink, active
            ? "Open another project before changing this project's folder."
            : "Choose a new Master folder for this project.");
        ToolTip.SetTip(_remove, active
            ? "Open another project before forgetting this project."
            : "Remove this project from the known-project list without deleting its files.");
    }

    private async Task RelinkSelectionAsync()
    {
        if (_projects.SelectedItem is not ProjectItem item) return;
        if (Project_Service.Instance.ActiveProject?.ProjectId == item.Manifest.ProjectId)
        {
            await RecoveryNotice_Window.Show(this, "Active project cannot be relinked",
                "Open another project before changing this project's folder.", item.Path, false);
            return;
        }
        List<string> selected = await FFXProjectEditor.Utils.AvaloniaDialog_Util.OpenFolderDialog(
            this, $"Select the Master folder for '{item.Name}'");
        if (selected.Count == 0) return;
        try
        {
            ProjectManifest updated = ProjectRegistry_Service.Relink(item.Manifest.ProjectId, selected[0]);
            int index = _manifests.FindIndex(project => project.ProjectId == updated.ProjectId);
            if (index >= 0) _manifests[index] = updated;
            _projects.ItemsSource = MakeItems();
            _projects.SelectedItem = _projects.Items.Cast<ProjectItem>()
                .FirstOrDefault(project => project.Manifest.ProjectId == updated.ProjectId);
        }
        catch (System.Exception ex)
        {
            await RecoveryNotice_Window.Show(this, "Project could not be relinked", ex.Message, selected[0], false);
        }
    }

    private async Task RemoveSelectionAsync()
    {
        if (_projects.SelectedItem is not ProjectItem item) return;
        if (Project_Service.Instance.ActiveProject?.ProjectId == item.Manifest.ProjectId)
        {
            await RecoveryNotice_Window.Show(this, "Active project cannot be forgotten",
                "Open another project before forgetting this project.", item.Path, false);
            return;
        }
        bool confirmed = await AiRevertConfirmationWindow.Show(this,
            "Forget This Project?",
            $"Zanarkand Workshop will forget '{item.Name}' and stop showing it as a known project.",
            item.Path,
            "Forget Project",
            "The FFX Master folder and its files will not be deleted. Workshop metadata will be archived for recovery.");
        if (!confirmed) return;
        try
        {
            ProjectRegistry_Service.ForgetProject(item.Manifest.ProjectId);
            _manifests.RemoveAll(project => project.ProjectId == item.Manifest.ProjectId);
            _projects.ItemsSource = MakeItems();
            _projects.SelectedIndex = _manifests.Count == 0 ? -1 : 0;
            RefreshActions();
        }
        catch (System.Exception ex)
        {
            await RecoveryNotice_Window.Show(this, "Project could not be removed", ex.Message, item.Path, false);
        }
    }

    private static T Place<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    public static Task<KnownProjectChoice?> Show(Window owner, IReadOnlyList<ProjectManifest> projects) =>
        new KnownProjects_Window(projects).ShowDialog<KnownProjectChoice?>(owner);
}
