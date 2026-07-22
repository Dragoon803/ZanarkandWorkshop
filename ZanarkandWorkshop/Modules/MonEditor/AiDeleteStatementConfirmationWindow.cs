using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MonEditor;

internal readonly record struct DeleteStatementConfirmationResult(bool Confirmed, bool DoNotShowAgain);

internal sealed class AiDeleteStatementConfirmationWindow : Window
{
    private readonly CheckBox _doNotShowAgain = new()
    {
        Content = "Don't show this deletion warning again"
    };

    private AiDeleteStatementConfirmationWindow(string explanation, string statementDescription)
    {
        const string title = "Delete Battle Logic Statement";
        Title = title;
        Width = 680;
        MinHeight = 350;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", MinWidth = 110 };
        var delete = new Button
        {
            Content = "Delete Statement", MinWidth = 170,
            Background = new SolidColorBrush(Color.Parse("#8B2F2F")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D95252")),
            BorderThickness = new Thickness(1)
        };
        cancel.Click += (_, _) => Close(new DeleteStatementConfirmationResult(false, false));
        delete.Click += (_, _) => Close(new DeleteStatementConfirmationResult(true, _doNotShowAgain.IsChecked == true));

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                    new TextBlock { Text = "Selected statement:", FontWeight = FontWeight.Bold },
                    new TextBox
                    {
                        Text = statementDescription, IsReadOnly = true,
                        TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas")
                    },
                    _doNotShowAgain,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, delete }
                    }
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(new DeleteStatementConfirmationResult(false, false));
        };
    }

    internal static Task<DeleteStatementConfirmationResult> Show(Window owner, string explanation,
        string statementDescription) =>
        new AiDeleteStatementConfirmationWindow(explanation, statementDescription)
            .ShowDialog<DeleteStatementConfirmationResult>(owner);
}
