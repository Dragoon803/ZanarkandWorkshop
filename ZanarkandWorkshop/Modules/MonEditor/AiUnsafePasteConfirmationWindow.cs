using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MonEditor;

internal readonly record struct UnsafePasteConfirmationResult(bool Confirmed, bool DoNotShowAgain);

internal sealed class AiUnsafePasteConfirmationWindow : Window
{
    private readonly CheckBox _doNotShowAgain = new()
    {
        Content = "Do not show this warning again"
    };

    private AiUnsafePasteConfirmationWindow(string issueDetails, string actionLabel)
    {
        const string title = "Review Before Continuing";
        Title = title;
        Width = 690;
        MinHeight = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 110,
            IsCancel = true
        };
        var paste = new Button
        {
            Content = actionLabel,
            MinWidth = 145,
            Background = new SolidColorBrush(Color.Parse("#8B5A22")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D99A45")),
            BorderThickness = new Thickness(1)
        };

        cancel.Click += (_, _) => Close(new UnsafePasteConfirmationResult(false, false));
        paste.Click += (_, _) =>
            Close(new UnsafePasteConfirmationResult(true, _doNotShowAgain.IsChecked == true));

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Bold },
                    new TextBlock
                    {
                        Text = "The copied logic contains references or control flow that may not work correctly in this location. " +
                               "Review the items below and manually correct them after pasting if necessary.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 14
                    },
                    new TextBlock { Text = "References that need attention:", FontWeight = FontWeight.Bold },
                    new TextBox
                    {
                        Text = issueDetails,
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Consolas"),
                        MinHeight = 80
                    },
                    new TextBlock
                    {
                        Text = "Do you want to continue?",
                        FontWeight = FontWeight.Bold
                    },
                    _doNotShowAgain,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, paste }
                    }
                }
            }
        };

        Opened += (_, _) => cancel.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(new UnsafePasteConfirmationResult(false, false));
        };
    }

    internal static Task<UnsafePasteConfirmationResult> Show(Window owner, string issueDetails,
        string actionLabel = "Paste Anyway") =>
        new AiUnsafePasteConfirmationWindow(issueDetails, actionLabel)
            .ShowDialog<UnsafePasteConfirmationResult>(owner);
}
