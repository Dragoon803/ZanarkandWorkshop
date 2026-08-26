using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FFXProjectEditor.Services;
using System;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.SphereGridEditor;

internal sealed class SphereGridCreationCaution_Window : Window
{
    private readonly CheckBox _dontShowAgain = new()
    {
        Content = "Don't show this warning again"
    };

    private SphereGridCreationCaution_Window(string structureName)
    {
        string structureNameLower = structureName.ToLowerInvariant();
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
                            Text = $"CAUTION - You can remove this newly created {structureNameLower} " +
                                   "with Undo or Undo All, as long as you have not saved the grid. " +
                                   $"Once the grid is saved, Zanarkand Workshop cannot delete the {structureNameLower}.",
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
        return AppSettings_Service.Current.Editors.SphereGrid.CreationCautionDismissed;
    }

    private static void SaveSuppressedPreference()
    {
        try
        {
            AppSettings_Service.Current.Editors.SphereGrid.CreationCautionDismissed = true;
            AppSettings_Service.Save();
        }
        catch
        {
            // Preference persistence is optional and must never interrupt editing.
        }
    }
}
