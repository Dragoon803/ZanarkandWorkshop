using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.SphereGrid;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.SphereGridEditor;

internal sealed record AddSphereGridNodeResult(
    SphereGridNodeTypeInfo NodeType,
    SphereGridCharacter Character,
    decimal X,
    decimal Y);

internal sealed class AddSphereGridNode_Window : Window
{
    private readonly ComboBox _nodeType;
    private readonly ComboBox _character;
    private readonly NumericUpDown _x;
    private readonly NumericUpDown _y;

    private AddSphereGridNode_Window(SphereGridEditor_DataModel model)
    {
        Title = "Create Sphere Grid Node";
        Width = 560;
        Height = 380;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nodeType = new ComboBox
        {
            Width = 240,
            ItemsSource = model.NodeTypeOptions,
            SelectedItem = model.NewNodeType ?? model.NodeTypeOptions[0],
            ItemTemplate = new FuncDataTemplate<SphereGridNodeTypeInfo>((item, _) =>
                new TextBlock { Text = item.Name })
        };
        _character = new ComboBox
        {
            Width = 180,
            ItemsSource = model.CharacterOptions,
            SelectedItem = model.NewNodeCharacter
        };
        _x = Coordinate(model.NewNodeX);
        _y = Coordinate(model.NewNodeY);

        var create = new Button { Content = "Create Node", MinWidth = 105 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        create.Click += (_, _) =>
        {
            if (_nodeType.SelectedItem is SphereGridNodeTypeInfo type &&
                _character.SelectedItem is SphereGridCharacter character)
                Close(new AddSphereGridNodeResult(type, character, _x.Value ?? 0, _y.Value ?? 0));
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
                    new TextBlock { Text = "Create Node", FontSize = 24, FontWeight = FontWeight.Bold },
                    new TextBlock
                    {
                        Text = $"The new node will reuse Cluster {model.SelectedNode!.ClusterIndex} from Node #{model.SelectedNode.Index}. It will not create a link.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.LightGray
                    },
                    Field("Node Type", _nodeType),
                    Field("Section Color", _character),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 18,
                        Children = { Field("Position X", _x), Field("Position Y", _y) }
                    },
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

    private static NumericUpDown Coordinate(decimal value) => new()
    {
        Width = 180,
        Value = value,
        Increment = 5,
        Minimum = short.MinValue,
        Maximum = short.MaxValue,
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

    internal static Task<AddSphereGridNodeResult?> Show(
        Window owner,
        SphereGridEditor_DataModel model) =>
        new AddSphereGridNode_Window(model).ShowDialog<AddSphereGridNodeResult?>(owner);
}
