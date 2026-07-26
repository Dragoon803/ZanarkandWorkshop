using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using static FFXProjectEditor.FfxLib.Ability.Ability_Command;

AssertRoundTrip(0x45);
AssertRoundTrip(0x0D);

Ability_Command editedSource = NewCommand(0x45);
KernelCommands_Wrapper editedWrapper = KernelCommands_Wrapper.Wrap(editedSource);
editedWrapper.FlagUsageLongRange = false;
byte editedValue = (byte)editedWrapper.Unwrap().UsageFlgs;
if (editedValue != 0x44)
    throw new InvalidOperationException(
        $"Editing a known flag lost preserved bits: expected 0x44, got 0x{editedValue:X2}.");

Console.WriteLine("Command UsageFlags round-trip checks passed.");

static void AssertRoundTrip(byte value)
{
    Ability_Command source = NewCommand(value);
    byte actual = (byte)KernelCommands_Wrapper.Wrap(source).Unwrap().UsageFlgs;
    if (actual != value)
        throw new InvalidOperationException(
            $"UsageFlags round trip failed: expected 0x{value:X2}, got 0x{actual:X2}.");
}

static Ability_Command NewCommand(byte usageFlags)
{
    return new Ability_Command
    {
        UsageFlgs = (UsageFlags)usageFlags,
        NameScriptBytes = [],
        UnusedText1ScriptBytes = [],
        DescriptionScriptBytes = [],
        UnusedText2ScriptBytes = [],
        ExtraInfo = new Ability_Command.ExtraCommandInfo()
    };
}
