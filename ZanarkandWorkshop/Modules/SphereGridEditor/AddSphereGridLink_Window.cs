using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.SphereGridEditor;

internal sealed record AddSphereGridLinkResult(int NodeA, int NodeB, int Anchor);

internal sealed class AddSphereGridLink_Window : Window
{
    private readonly NumericUpDown _nodeA;
    private readonly NumericUpDown _nodeB;
    private readonly NumericUpDown _anchor;
    private readonly TextBlock _validation;

    private AddSphereGridLink_Window(SphereGridEditor_DataModel model)
    {
        Title = "Create Sphere Grid Link";
        Width = 560;
        Height = 350;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        int maximumNode = Math.Max(0, (model.Graph?.File.Nodes.Count ?? 1) - 1);
        _nodeA = Index(0, maximumNode);
        _nodeB = Index(Math.Min(1, maximumNode), maximumNode);
        _anchor = Index(ushort.MaxValue, ushort.MaxValue);
        _validation = new TextBlock
        {
            Foreground = Brushes.Orange,
            TextWrapping = TextWrapping.Wrap
        };

        var create = new Button { Content = "Create Link", MinWidth = 105 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        create.Click += (_, _) =>
        {
            int a = Decimal.ToInt32(_nodeA.Value ?? 0);
            int b = Decimal.ToInt32(_nodeB.Value ?? 0);
            int anchor = Decimal.ToInt32(_anchor.Value ?? ushort.MaxValue);
            if (!model.TryValidateNewLink(a, b, anchor, out string message))
            {
                _validation.Text = message;
                return;
            }
            Close(new AddSphereGridLinkResult(a, b, anchor));
        };
        cancel.Click += (_, _) => Close(null);

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Create Link", FontSize = 24, FontWeight = FontWeight.Bold },
                    new TextBlock
                    {
                        Text = "Choose the two nodes to connect. Use 65535 as the anchor for a straight link.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.LightGray
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 18,
                        Children = { Field("Node A ID", _nodeA), Field("Node B ID", _nodeB), Field("Anchor", _anchor) }
                    },
                    _validation,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, create }
                    }
                }
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(null);
            }
        };
    }

    private static NumericUpDown Index(decimal value, decimal maximum) => new()
    {
        Width = 140,
        Value = value,
        Increment = 1,
        Minimum = 0,
        Maximum = maximum,
        FormatString = "0"
    };

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontWeight = FontWeight.Bold },
            control
        }
    };

    internal static Task<AddSphereGridLinkResult?> Show(
        Window owner, SphereGridEditor_DataModel model) =>
        new AddSphereGridLink_Window(model).ShowDialog<AddSphereGridLinkResult?>(owner);
}
