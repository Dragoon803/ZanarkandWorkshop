using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MonEditor;

internal sealed class AiMessageDetailsWindow : Window
{
    private AiMessageDetailsWindow(string title, string message)
    {
        Title = title;
        Width = 660;
        MinHeight = 330;
        MaxHeight = 700;
        SizeToContent = SizeToContent.Height;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var close = new Button
        {
            Content = "Close",
            Padding = new Thickness(18, 6),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => Close();

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        var explanation = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 21
            }
        };
        close.Margin = new Thickness(0, 16, 0, 0);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { heading, explanation, close }
        };
        Grid.SetRow(heading, 0);
        Grid.SetRow(explanation, 1);
        Grid.SetRow(close, 2);

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = layout
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    internal static Task Show(Window owner, string title, string message) =>
        new AiMessageDetailsWindow(title, message).ShowDialog(owner);
}
