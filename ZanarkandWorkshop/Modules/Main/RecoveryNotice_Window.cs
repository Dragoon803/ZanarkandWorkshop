using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace FFXProjectEditor;

internal sealed class RecoveryNotice_Window : Window
{
	private RecoveryNotice_Window(string title, string message, string? path, bool success)
	{
		Title = title;
		Width = 620;
		SizeToContent = SizeToContent.Height;
		CanResize = false;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		Background = Brushes.Black;

		var closeButton = new Button
		{
			Content = "OK",
			Padding = new Thickness(22, 7),
			HorizontalAlignment = HorizontalAlignment.Right
		};
		closeButton.Click += (_, _) => Close();

		var content = new StackPanel { Spacing = 14 };
		content.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 22,
			FontWeight = FontWeight.Bold,
			Foreground = new SolidColorBrush(Color.Parse(success ? "#55DD77" : "#FF6666"))
		});
		content.Children.Add(new TextBlock
		{
			Text = message,
			FontSize = 15,
			TextWrapping = TextWrapping.Wrap
		});

		if (!string.IsNullOrWhiteSpace(path))
		{
			content.Children.Add(new TextBlock { Text = "Selected folder:", FontWeight = FontWeight.Bold });
			content.Children.Add(new TextBox
			{
				Text = path,
				IsReadOnly = true,
				FontFamily = FontFamily.Parse("Consolas"),
				TextWrapping = TextWrapping.Wrap
			});
		}

		content.Children.Add(closeButton);
		Content = new Border
		{
			Padding = new Thickness(24),
			BorderBrush = new SolidColorBrush(Color.Parse(success ? "#3D8850" : "#AA3333")),
			BorderThickness = new Thickness(1),
			Child = content
		};

		KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape || e.Key == Key.Enter)
				Close();
		};
	}

	public static Task Show(Window owner, string title, string message, string? path, bool success)
		=> new RecoveryNotice_Window(title, message, path, success).ShowDialog(owner);
}
