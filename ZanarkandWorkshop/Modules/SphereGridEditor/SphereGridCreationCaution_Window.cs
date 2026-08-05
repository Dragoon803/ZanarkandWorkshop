using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.SphereGridEditor;

internal sealed class SphereGridCreationCaution_Window : Window
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "sphere-grid-editor-preferences.json");

    private readonly CheckBox _dontShowAgain = new()
    {
        Content = "Don't show this warning again"
    };

    private SphereGridCreationCaution_Window(string structureName)
    {
        Title = $"Create Sphere Grid {structureName}?";
        Width = 610;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var proceed = new Button { Content = $"Create {structureName}", MinWidth = 110 };
        cancel.Click += (_, _) => Close(false);
        proceed.Click += (_, _) =>
        {
            if (_dontShowAgain.IsChecked == true)
                SaveSuppressedPreference();
            Close(true);
        };

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Create Sphere Grid {structureName}?",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold
                    },
                    new Border
                    {
                        Padding = new Thickness(12),
                        Background = new SolidColorBrush(Color.Parse("#33240F")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#D99A2B")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Child = new TextBlock
                        {
                            Text = "CAUTION - Zanarkand Workshop cannot delete nodes or links. " +
                                   "Undo can still remove created nodes and link before saving. " +
                                   "After saving they will be a permanent part of your sphere grid. " +
                                   "Use with caution.",
                            Foreground = new SolidColorBrush(Color.Parse("#FFBE55")),
                            FontWeight = FontWeight.Bold,
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    _dontShowAgain,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, proceed }
                    }
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
        };
    }

    internal static Task<bool> Confirm(Window owner, string structureName)
    {
        if (IsSuppressed())
            return Task.FromResult(true);
        return new SphereGridCreationCaution_Window(structureName).ShowDialog<bool>(owner);
    }

    private static bool IsSuppressed()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return false;
            SphereGridPreferences? preferences = JsonSerializer.Deserialize<SphereGridPreferences>(
                File.ReadAllText(PreferencesPath));
            return preferences?.SuppressCreationCaution == true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveSuppressedPreference()
    {
        try
        {
            string? directory = Path.GetDirectoryName(PreferencesPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                PreferencesPath,
                JsonSerializer.Serialize(new SphereGridPreferences(true)));
        }
        catch
        {
            // Preference persistence is optional and must never interrupt editing.
        }
    }

    private sealed record SphereGridPreferences(bool SuppressCreationCaution);
}
