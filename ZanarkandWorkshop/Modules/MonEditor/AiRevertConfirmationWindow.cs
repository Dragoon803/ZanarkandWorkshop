using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MonEditor;

internal sealed class AiRevertConfirmationWindow : Window
{
    private AiRevertConfirmationWindow(string title, string explanation, string source, string revertLabel,
        string diskNotice = "Nothing is written to disk until you press Save in the monster editor.",
        bool showSource = true)
    {
        Title = title;
        Width = 680;
        MinHeight = 320;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 5)
        };
        var revert = new Button
        {
            Content = revertLabel, Padding = new Thickness(12, 5),
            Background = new SolidColorBrush(Color.Parse("#8B2F2F")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D95252")), BorderThickness = new Thickness(1)
        };
        cancel.Click += (_, _) => Close(false);
        revert.Click += (_, _) => Close(true);

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock
            { Text = title, FontSize = 20, FontWeight = FontWeight.Bold });
        content.Children.Add(new TextBlock
            { Text = explanation, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        if (showSource)
        {
            content.Children.Add(new TextBlock { Text = "Source:", FontWeight = FontWeight.Bold });
            content.Children.Add(new TextBox
            {
                Text = source,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas")
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = diskNotice,
            Foreground = new SolidColorBrush(Color.Parse("#FFB35C")),
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancel, revert }
        });

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = content
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(false); }
        };
    }

    internal static Task<bool> Show(Window owner, string title, string explanation, string source, string revertLabel,
        string diskNotice = "Nothing is written to disk until you press Save in the monster editor.") =>
        new AiRevertConfirmationWindow(title, explanation, source, revertLabel, diskNotice).ShowDialog<bool>(owner);

    internal static Task<bool> ShowWithoutSource(Window owner, string title, string explanation, string revertLabel,
        string diskNotice) =>
        new AiRevertConfirmationWindow(title, explanation, "", revertLabel, diskNotice, false)
            .ShowDialog<bool>(owner);
}
