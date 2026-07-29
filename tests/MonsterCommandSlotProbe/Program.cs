using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: MonsterCommandSlotProbe <source.bin> <output.bin> <clone-index> <category>");
    return 2;
}

string sourcePath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[1]);
int cloneIndex = int.Parse(args[2]);
int category = Convert.ToInt32(args[3], 16);

if (category is not (0x4 or 0x6))
    throw new ArgumentOutOfRangeException(nameof(category), "Category must be 4 or 6.");

byte[] sourceBytes = File.ReadAllBytes(sourcePath);
EntryListFile sourcePacked = EntryListFile.Unpack(sourceBytes);
List<Ability_Command> original =
    Ability_Command.ReadList(sourceBytes, hasExtraInfo: false);

if (sourcePacked.Header.EntrySize != 0x5C)
    throw new InvalidDataException(
        $"Expected monmagic entry size 0x5C; found 0x{sourcePacked.Header.EntrySize:X}.");
if (cloneIndex < 0 || cloneIndex >= original.Count)
    throw new ArgumentOutOfRangeException(
        nameof(cloneIndex), $"Clone index must be between 0 and {original.Count - 1}.");
if (original.Count >= 0x1000)
    throw new InvalidDataException("No 12-bit monster-command indices remain.");

int newIndex = original.Count;
Ability_Command appended = Clone(original[cloneIndex]);
List<Ability_Command> expanded = new(original) { appended };
byte[] rebuilt = Ability_Command.WriteList(expanded, hasExtraInfo: false);

EntryListFile rebuiltPacked = EntryListFile.Unpack(rebuilt);
List<Ability_Command> verified =
    Ability_Command.ReadList(rebuilt, hasExtraInfo: false);

if (verified.Count != original.Count + 1)
    throw new InvalidDataException(
        $"Expected {original.Count + 1} entries; found {verified.Count}.");
if (rebuiltPacked.Header.EntrySize != 0x5C)
    throw new InvalidDataException("The rebuilt entry size is not 0x5C.");
if (rebuiltPacked.FirstFile.Length != verified.Count * 0x5C)
    throw new InvalidDataException("The rebuilt command table has an unexpected length.");

for (int index = 0; index < original.Count; index++)
{
    byte[] before = original[index].WriteSingle(hasExtraInfo: false);
    byte[] after = verified[index].WriteSingle(hasExtraInfo: false);
    if (!before.SequenceEqual(after))
        throw new InvalidDataException(
            $"Existing monster command {index} changed during append verification.");
}

byte[] expectedClone = appended.WriteSingle(hasExtraInfo: false);
byte[] actualClone = verified[newIndex].WriteSingle(hasExtraInfo: false);
if (!expectedClone.SequenceEqual(actualClone))
    throw new InvalidDataException("The appended monster command did not round-trip exactly.");

string name = FfxEncoding.DecodeEditableTextScript(
    verified[newIndex].NameScriptBytes, FfxEncoding.UsDecoder);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, rebuilt);

Console.WriteLine($"Source entries : {original.Count}");
Console.WriteLine($"Output entries : {verified.Count}");
Console.WriteLine($"Cloned index   : {cloneIndex} ({name})");
Console.WriteLine($"New index      : {newIndex} (0x{newIndex:X3})");
Console.WriteLine($"Command ref    : 0x{((category << 12) | newIndex):X4}");
Console.WriteLine($"Source bytes   : {sourceBytes.Length}");
Console.WriteLine($"Output bytes   : {rebuilt.Length}");
Console.WriteLine($"Output          : {outputPath}");
Console.WriteLine("Monster-command append and preservation checks passed.");
return 0;

static Ability_Command Clone(Ability_Command source) =>
    Ability_Command.ReadSingle(
        source.WriteSingle(hasExtraInfo: false), hasExtraInfo: false);
