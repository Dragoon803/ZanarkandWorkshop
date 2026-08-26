using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using FFXProjectEditor.Services;
using System;
using System.Threading.Tasks;

namespace FFXProjectEditor;

internal sealed class ProjectName_Window : Window
{
    private readonly TextBox _name = new();
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.OrangeRed };

    private ProjectName_Window(string suggestedName)
    {
        Title = "Name Project";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _name.Text = suggestedName;

        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var create = new Button { Content = "Create Project", MinWidth = 110 };
        cancel.Click += (_, _) => Close(null);
        create.Click += (_, _) => Accept();

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Name this project", FontSize = 21, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = "The name identifies this Master folder in Zanarkand Workshop and is used for its program metadata folder.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    _name,
                    _error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, create } }
                }
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(null);
            else if (e.Key == Key.Enter) Accept();
        };
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private void Accept()
    {
        try { Close(ProjectRegistry_Service.ValidateNewName(_name.Text ?? "")); }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    public static Task<string?> Show(Window owner, string suggestedName) =>
        new ProjectName_Window(suggestedName).ShowDialog<string?>(owner);
}
