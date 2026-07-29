using Avalonia;
using Avalonia.Controls.Primitives;

namespace FFXProjectEditor;

public class Property_Template : TemplatedControl
{
    public static readonly StyledProperty<string> Property_LabelProperty = AvaloniaProperty.Register<TemplatedControl, string>(nameof(Property_Label), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public string Property_Label
    {
        get => GetValue(Property_LabelProperty);
        set => SetValue(Property_LabelProperty, value);
    }
    public static readonly StyledProperty<decimal?> Property_ValueProperty = AvaloniaProperty.Register<TemplatedControl, decimal?>(nameof(Property_Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public decimal? Property_Value
    {
        get => GetValue(Property_ValueProperty);
        set => SetValue(Property_ValueProperty, value);
    }
    public static readonly StyledProperty<decimal> MinimumProperty =
        AvaloniaProperty.Register<TemplatedControl, decimal>(nameof(Minimum), 0);
    public decimal Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public static readonly StyledProperty<decimal> MaximumProperty =
        AvaloniaProperty.Register<TemplatedControl, decimal>(nameof(Maximum), 255);
    public decimal Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public static readonly StyledProperty<int> MaxInputLengthProperty =
        AvaloniaProperty.Register<TemplatedControl, int>(nameof(MaxInputLength), 3);
    public int MaxInputLength { get => GetValue(MaxInputLengthProperty); set => SetValue(MaxInputLengthProperty, value); }
    public static readonly StyledProperty<int> BorderWidthProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(BorderWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public int BorderWidth
    {
        get => GetValue(BorderWidthProperty);
        set => SetValue(BorderWidthProperty, value);
    }
    public static readonly StyledProperty<int> ValueBorderWidthProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(ValueBorderWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, defaultValue:100);
    public int ValueBorderWidth
    {
        get => GetValue(ValueBorderWidthProperty);
        set => SetValue(ValueBorderWidthProperty, value);
    }
}
