using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Main;

internal enum PendingChangesDecision
{
    Cancel,
    Discard,
    Save
}

internal sealed class PendingChanges_Window : Window
{
    private PendingChanges_Window(string context)
    {
        Title = "Unsaved Changes";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6) };
        var discard = new Button
        {
            Content = "Discard",
            Padding = new Thickness(14, 6),
            Background = new SolidColorBrush(Color.Parse("#8B2F2F"))
        };
        var save = new Button { Content = "Save", Padding = new Thickness(20, 6) };
        cancel.Click += (_, _) => Close(PendingChangesDecision.Cancel);
        discard.Click += (_, _) => Close(PendingChangesDecision.Discard);
        save.Click += (_, _) => Close(PendingChangesDecision.Save);

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Save changes before continuing?",
                        FontSize = 21,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = $"The current editor has unsaved changes. {context}",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Discard permanently removes the unsaved changes.",
                        Foreground = new SolidColorBrush(Color.Parse("#FFB35C")),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, discard, save }
                    }
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(PendingChangesDecision.Cancel);
        };
    }

    internal static Task<PendingChangesDecision> Show(Window owner, string context) =>
        new PendingChanges_Window(context).ShowDialog<PendingChangesDecision>(owner);
}
