using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MonEditor;

internal enum PartialStatementRepairChoice { Cancel, RestoreStatement, DeleteStatement }

internal sealed class AiPartialStatementRepairWindow : Window
{
    private AiPartialStatementRepairWindow(ManualAiPartialStatementException issue)
    {
        Title = "Repair Incomplete Battle Logic";
        Width = 720;
        MinHeight = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var restore = new Button { Content = "Restore Complete Statement", MinWidth = 190 };
        var delete = new Button
        {
            Content = "Delete Complete Statement", MinWidth = 190,
            Background = new SolidColorBrush(Color.Parse("#8B2F2F")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D95252")), BorderThickness = new Thickness(1)
        };
        cancel.Click += (_, _) => Close(PartialStatementRepairChoice.Cancel);
        restore.Click += (_, _) => Close(PartialStatementRepairChoice.RestoreStatement);
        delete.Click += (_, _) => Close(PartialStatementRepairChoice.DeleteStatement);

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Incomplete Battle Logic statement", FontSize = 20, FontWeight = FontWeight.Bold },
                    new TextBlock
                    {
                        Text = $"Bytes {issue.DeletedBytes} were removed from the Battle Logic statement at 0x{issue.StatementOffset:X4}. " +
                               "The remaining instructions can leave an intermediate value on the script stack or leave a condition without its branch.",
                        TextWrapping = TextWrapping.Wrap, FontSize = 14
                    },
                    new TextBlock { Text = "Original Battle Logic:", FontWeight = FontWeight.Bold },
                    new TextBox { Text = issue.StatementTranslation, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") },
                    new TextBlock
                    {
                        Text = "Restore Complete Statement puts the removed bytes back. Delete Complete Statement safely removes the entire group and rebuilds all later offsets.",
                        TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#FFB35C")), FontWeight = FontWeight.Bold
                    },
                    new TextBlock { Text = "Nothing is written to disk until you press Save.", TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10, Children = { cancel, restore, delete }
                    }
                }
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(PartialStatementRepairChoice.Cancel); }
        };
    }

    internal static Task<PartialStatementRepairChoice> Show(Window owner, ManualAiPartialStatementException issue) =>
        new AiPartialStatementRepairWindow(issue).ShowDialog<PartialStatementRepairChoice>(owner);
}
