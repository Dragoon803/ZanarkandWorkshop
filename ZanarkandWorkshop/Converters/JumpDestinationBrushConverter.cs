using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FFXProjectEditor.Converters;

public sealed class JumpDestinationBrushConverter : IValueConverter
{
    private static readonly IBrush DestinationBrush = new SolidColorBrush(Color.Parse("#2C6EAC"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? DestinationBrush : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class JumpDestinationForegroundConverter : IValueConverter
{
    private static readonly IBrush HexBrush = new SolidColorBrush(Color.Parse("#70B7FF"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Brushes.White : HexBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
