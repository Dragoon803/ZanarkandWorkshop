using Avalonia.Controls;

namespace FFXProjectEditor;

public partial class EditorHelp_Window : Window
{
    public EditorHelp_Window()
    {
        InitializeComponent();
    }

    private void Button_Close(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
