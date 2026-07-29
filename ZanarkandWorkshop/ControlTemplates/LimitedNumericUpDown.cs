using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace FFXProjectEditor;

public class LimitedNumericUpDown : NumericUpDown
{
    private TextBox? _textBox;
    private decimal? _valueAtFocus;

    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    public LimitedNumericUpDown()
    {
        ClipValueToMinMax = true;
    }

    public static readonly StyledProperty<int> MaxInputLengthProperty =
        AvaloniaProperty.Register<LimitedNumericUpDown, int>(nameof(MaxInputLength), 3);

    public int MaxInputLength
    {
        get => GetValue(MaxInputLengthProperty);
        set => SetValue(MaxInputLengthProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_textBox is not null)
        {
            _textBox.GotFocus -= TextBox_GotFocus;
            _textBox.KeyDown -= TextBox_KeyDown;
        }

        base.OnApplyTemplate(e);

        _textBox = e.NameScope.Find<TextBox>("PART_TextBox");
        if (_textBox is null)
            return;

        _textBox.MaxLength = MaxInputLength;
        _textBox.GotFocus += TextBox_GotFocus;
        _textBox.KeyDown += TextBox_KeyDown;
    }

    private void TextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _valueAtFocus = Value;
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitText();
            _valueAtFocus = Value;
            _textBox?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Value = _valueAtFocus;
            UpdateTextFromValue();
            _textBox?.SelectAll();
            e.Handled = true;
        }
    }

    private void CommitText()
    {
        if (_textBox is null)
            return;

        if (!decimal.TryParse(
                _textBox.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out decimal enteredValue))
        {
            UpdateTextFromValue();
            return;
        }

        Value = Math.Clamp(enteredValue, Minimum, Maximum);
        UpdateTextFromValue();
    }

    private void UpdateTextFromValue()
    {
        if (_textBox is not null)
            _textBox.Text = Value?.ToString("0", CultureInfo.CurrentCulture) ?? string.Empty;
    }
}
