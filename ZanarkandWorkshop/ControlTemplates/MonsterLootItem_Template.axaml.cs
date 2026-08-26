using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FFXProjectEditor.Modules;
using FFXProjectEditor.Modules.MonEditor;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor;

internal class MonsterLootItem_Template : TemplatedControl
{
    private ComboBox? _itemComboBox;

    public static readonly StyledProperty<string> LootLabelProperty =
        AvaloniaProperty.Register<MonsterLootItem_Template, string>(nameof(LootLabel));
    public string LootLabel
    {
        get => GetValue(LootLabelProperty);
        set => SetValue(LootLabelProperty, value);
    }

    public static readonly StyledProperty<GameIndex_Wrapper> LootObjectProperty =
        AvaloniaProperty.Register<MonsterLootItem_Template, GameIndex_Wrapper>(
            nameof(LootObject), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public GameIndex_Wrapper LootObject
    {
        get => GetValue(LootObjectProperty);
        set => SetValue(LootObjectProperty, value);
    }

    public static readonly StyledProperty<ushort> LootCountProperty =
        AvaloniaProperty.Register<MonsterLootItem_Template, ushort>(
            nameof(LootCount), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public ushort LootCount
    {
        get => GetValue(LootCountProperty);
        set => SetValue(LootCountProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<MonsterLootItemOption>> ItemOptionsProperty =
        AvaloniaProperty.Register<MonsterLootItem_Template, IReadOnlyList<MonsterLootItemOption>>(
            nameof(ItemOptions));
    public IReadOnlyList<MonsterLootItemOption> ItemOptions
    {
        get => GetValue(ItemOptionsProperty);
        set => SetValue(ItemOptionsProperty, value);
    }

    public static readonly StyledProperty<double> ItemDropdownWidthProperty =
        AvaloniaProperty.Register<MonsterLootItem_Template, double>(nameof(ItemDropdownWidth));
    public double ItemDropdownWidth
    {
        get => GetValue(ItemDropdownWidthProperty);
        set => SetValue(ItemDropdownWidthProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_itemComboBox is not null)
            _itemComboBox.SelectionChanged -= ItemComboBox_SelectionChanged;

        base.OnApplyTemplate(e);
        _itemComboBox = e.NameScope.Find<ComboBox>("PART_ItemComboBox");
        if (_itemComboBox is null)
            return;

        _itemComboBox.SelectionChanged += ItemComboBox_SelectionChanged;
        ResizeItemComboBox();
    }

    private void ItemComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ResizeItemComboBox();
    }

    private void ResizeItemComboBox()
    {
        if (_itemComboBox is not null && ItemDropdownWidth > 0)
        {
            _itemComboBox.Width = ItemDropdownWidth;
            return;
        }

        if (_itemComboBox?.SelectedItem is not MonsterLootItemOption selectedItem)
            return;

        TextBlock measurement = new() { Text = selectedItem.DisplayName };
        measurement.Measure(Size.Infinity);
        _itemComboBox.Width = Math.Clamp(measurement.DesiredSize.Width + 80, 120, 300);
    }
}
