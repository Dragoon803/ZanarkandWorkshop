using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FFXProjectEditor.Services;
using System.Threading.Tasks;

namespace FFXProjectEditor;

internal sealed class RecoveryVerification_Window : Window
{
    private RecoveryVerification_Window(VanillaReference_Service.ValidationResult result, string path, bool resultsOnly = false)
    {
        Title = result.Classification;
        Width = 760;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6) };
        var accept = new Button
        {
            Content = resultsOnly ? "Close" : "Use Unrecognized Source",
            Padding = new Thickness(14, 6),
            Background = new SolidColorBrush(Color.Parse("#8B5A20"))
        };
        var copy = new Button { Content = "Copy Diagnostics", Padding = new Thickness(14, 6) };
        cancel.Click += (_, _) => Close(false);
        accept.Click += (_, _) => Close(true);
        copy.Click += async (_, _) =>
        {
            if (Clipboard is not null)
                await Clipboard.SetTextAsync(VanillaReference_Service.BuildDiagnostics(result));
        };

        var content = new StackPanel { Spacing = 13 };
        content.Children.Add(new TextBlock
        {
            Text = result.Classification,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = result.IsKnownReference ? Brushes.LightGreen : Brushes.Orange
        });
        content.Children.Add(new TextBlock { Text = result.Summary, TextWrapping = TextWrapping.Wrap });
        if (!resultsOnly)
            content.Children.Add(new TextBlock
            {
                Text = "Zanarkand Workshop cannot confirm that this source matches a known original reference. " +
                       "This does not prove the files are modified or corrupted. Continue only if you are confident " +
                       "the folder came from a clean game extraction. Acceptance lasts for this application session " +
                       "and does not change the verification label.",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#FFB35C"))
            });
        content.Children.Add(new TextBlock { Text = "Selected folder:", FontWeight = FontWeight.Bold });
        content.Children.Add(new TextBox { Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        buttons.Children.Add(copy);
        if (!resultsOnly) buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        content.Children.Add(buttons);
        Content = new ScrollViewer { Content = new Border { Padding = new Thickness(24), Child = content } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    public static Task<bool> Show(Window owner, VanillaReference_Service.ValidationResult result, string path) =>
        new RecoveryVerification_Window(result, path).ShowDialog<bool>(owner);

    public static Task<bool> ShowResults(Window owner, VanillaReference_Service.ValidationResult result, string path) =>
        new RecoveryVerification_Window(result, path, resultsOnly: true).ShowDialog<bool>(owner);
}
