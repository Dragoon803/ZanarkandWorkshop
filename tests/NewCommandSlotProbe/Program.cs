using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine(
        "Usage: NewCommandSlotProbe <source-command.bin> [output-command.bin] [clone-index]");
    return 2;
}

string sourcePath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args.Length >= 2
    ? args[1]
    : Path.Combine(Environment.CurrentDirectory, "command-expanded-phase1.bin"));

byte[] sourceBytes = File.ReadAllBytes(sourcePath);
EntryListFile sourcePacked = EntryListFile.Unpack(sourceBytes);
List<Ability_Command> original = Ability_Command.ReadList(sourceBytes, hasExtraInfo: true);

if (sourcePacked.Header.EntrySize != 0x60)
    throw new InvalidDataException(
        $"Expected command.bin entry size 0x60; found 0x{sourcePacked.Header.EntrySize:X}.");
if (original.Count == 0)
    throw new InvalidDataException("The source command.bin contains no commands.");
if (original.Count >= 0x1000)
    throw new InvalidDataException("No 12-bit command indices remain.");

int cloneIndex = args.Length == 3 ? int.Parse(args[2]) : 0;
if (cloneIndex < 0 || cloneIndex >= original.Count)
    throw new ArgumentOutOfRangeException(
        nameof(cloneIndex), $"Clone index must be between 0 and {original.Count - 1}.");

int newIndex = original.Count;
Ability_Command appended = Clone(original[cloneIndex]);
if (args.Length < 3)
{
    appended.NameScriptBytes =
        FfxEncoding.EncodeTextScript($"Slot {newIndex} Probe", FfxEncoding.UsEncoder);
    appended.DescriptionScriptBytes =
        FfxEncoding.EncodeTextScript(
            "Append-only command-table expansion test.", FfxEncoding.UsEncoder);
}

List<Ability_Command> expanded = new(original) { appended };
byte[] rebuilt = Ability_Command.WriteList(expanded, hasExtraInfo: true);
List<Ability_Command> verified =
    Ability_Command.ReadList(rebuilt, hasExtraInfo: true);
EntryListFile rebuiltPacked = EntryListFile.Unpack(rebuilt);

if (verified.Count != original.Count + 1)
    throw new InvalidDataException(
        $"Expected {original.Count + 1} entries after rebuild; found {verified.Count}.");
if (rebuiltPacked.Header.EntrySize != 0x60)
    throw new InvalidDataException("The rebuilt entry size is not 0x60.");
if (rebuiltPacked.Header.RealEntryCount != verified.Count)
    throw new InvalidDataException("The rebuilt header count does not match the decoded list.");
if (rebuiltPacked.FirstFile.Length != verified.Count * 0x60)
    throw new InvalidDataException("The rebuilt command table has an unexpected byte length.");

for (int index = 0; index < original.Count; index++)
{
    byte[] before = original[index].WriteSingle(hasExtraInfo: true);
    byte[] after = verified[index].WriteSingle(hasExtraInfo: true);
    if (!before.SequenceEqual(after))
        throw new InvalidDataException(
            $"Existing command {index} changed during append/rebuild verification.");
}

byte[] expectedClone = appended.WriteSingle(hasExtraInfo: true);
byte[] actualClone = verified[newIndex].WriteSingle(hasExtraInfo: true);
if (!expectedClone.SequenceEqual(actualClone))
    throw new InvalidDataException("The appended command did not round-trip exactly.");

string actualName = FfxEncoding.DecodeEditableTextScript(
    verified[newIndex].NameScriptBytes, FfxEncoding.UsDecoder);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, rebuilt);

Console.WriteLine($"Source entries : {original.Count}");
Console.WriteLine($"Output entries : {verified.Count}");
Console.WriteLine($"New index      : {newIndex} (0x{newIndex:X3})");
Console.WriteLine($"Command ref    : 0x{(0x3000 | newIndex):X4}");
Console.WriteLine($"Cloned index   : {cloneIndex} ({actualName})");
Console.WriteLine($"Source bytes   : {sourceBytes.Length}");
Console.WriteLine($"Output bytes   : {rebuilt.Length}");
Console.WriteLine($"Output          : {outputPath}");
Console.WriteLine("Phase 1 append and decoded-record preservation checks passed.");
return 0;

static Ability_Command Clone(Ability_Command source) =>
    Ability_Command.ReadSingle(source.WriteSingle(hasExtraInfo: true), hasExtraInfo: true);
