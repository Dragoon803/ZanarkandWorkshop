namespace FFXProjectEditor.Modules.MonEditor;

internal sealed record MonsterLootItemOption(ushort ItemId, string DisplayName)
{
    public override string ToString() => DisplayName;
}
