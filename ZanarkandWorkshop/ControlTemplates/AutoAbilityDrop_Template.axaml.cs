using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using FFXProjectEditor.Modules;
using System;
using System.Linq;

namespace FFXProjectEditor;

public class AutoAbilityDrop_Template : TemplatedControl
{
    private ComboBox? _abilityCombo;
    private string _searchText = "";
    private DateTime _lastSearchInput;

    public static readonly StyledProperty<string> AbilityLabelProperty =
        AvaloniaProperty.Register<AutoAbilityDrop_Template, string>(nameof(AbilityLabel));

    public string AbilityLabel
    {
        get => GetValue(AbilityLabelProperty);
        set => SetValue(AbilityLabelProperty, value);
    }

    public static readonly StyledProperty<GameIndex_Wrapper> AbilityProperty =
        AvaloniaProperty.Register<AutoAbilityDrop_Template, GameIndex_Wrapper>(nameof(Ability));

    public GameIndex_Wrapper Ability
    {
        get => GetValue(AbilityProperty);
        set => SetValue(AbilityProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_abilityCombo != null)
        {
            _abilityCombo.DropDownOpened -= Ability_DropDownOpened;
            _abilityCombo.KeyDown -= Ability_KeyDown;
        }
        base.OnApplyTemplate(e);
        _abilityCombo = e.NameScope.Find<ComboBox>("PART_AutoAbility");
        if (_abilityCombo == null) return;
        _abilityCombo.DropDownOpened += Ability_DropDownOpened;
        _abilityCombo.KeyDown += Ability_KeyDown;
    }

    private static void Ability_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem == null) return;
        Dispatcher.UIThread.Post(() => combo.ScrollIntoView(combo.SelectedItem), DispatcherPriority.Loaded);
    }

    private void Ability_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ComboBox combo || !combo.IsDropDownOpen ||
            e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        if ((DateTime.UtcNow - _lastSearchInput).TotalMilliseconds > 900) _searchText = "";
        _lastSearchInput = DateTime.UtcNow;
        string key = e.Key.ToString();
        if (e.Key == Key.Back) _searchText = _searchText.Length == 0 ? "" : _searchText[..^1];
        else if (key.Length == 1 && char.IsLetterOrDigit(key[0])) _searchText += key;
        else if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) _searchText += key[1];
        else return;
        object? match = combo.ItemsSource?.Cast<object>().FirstOrDefault(item =>
            item.ToString()?.StartsWith(_searchText, StringComparison.OrdinalIgnoreCase) == true)
            ?? combo.ItemsSource?.Cast<object>().FirstOrDefault(item =>
                item.ToString()?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true);
        if (match == null) return;
        combo.SelectedItem = match;
        combo.ScrollIntoView(match);
        e.Handled = true;
    }
}
