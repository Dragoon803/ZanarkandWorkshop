using System.Diagnostics;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Services;

if (args.Length != 1)
{
    Console.Error.WriteLine(
        "Usage: InGameCommandSlotProbe <loaded-command.bin>");
    return 2;
}

string candidatePath = Path.GetFullPath(args[0]);
byte[] candidate = File.ReadAllBytes(candidatePath);
EntryListFile expected = EntryListFile.Unpack(candidate);

Process? game = Process.GetProcessesByName("FFX").SingleOrDefault();
if (game is null)
    throw new InvalidOperationException("FFX is not running.");

Process_Service.Instance.SetProcess(game);
int commandFileAddress =
    MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_COMMAND);
if (commandFileAddress == 0)
    throw new InvalidDataException("The game command-file pointer is null.");

byte[] headerBytes =
    MemSharp_Service.Instance.Read<byte>(commandFileAddress, 0x14, isRelative: false);
EntryListFile loadedHeader = EntryListFile.Unpack(headerBytes);

int tableLength = expected.Header.RealEntryCount * expected.Header.EntrySize;
byte[] loadedTable = MemSharp_Service.Instance.Read<byte>(
    commandFileAddress + 0x14, tableLength, isRelative: false);

if (loadedHeader.Header.RealEntryCount != expected.Header.RealEntryCount)
    throw new InvalidDataException(
        $"Game loaded {loadedHeader.Header.RealEntryCount} entries; " +
        $"expected {expected.Header.RealEntryCount}.");
if (loadedHeader.Header.EntrySize != 0x60)
    throw new InvalidDataException(
        $"Game reports unexpected entry size 0x{loadedHeader.Header.EntrySize:X}.");
if (!loadedTable.SequenceEqual(expected.FirstFile))
    throw new InvalidDataException(
        "The in-memory command table differs from the candidate command table.");

int newIndex = expected.Header.RealEntryCount - 1;
int newRecordOffset = newIndex * expected.Header.EntrySize;
byte[] expectedNewRecord =
    expected.FirstFile.AsSpan(newRecordOffset, expected.Header.EntrySize).ToArray();
byte[] loadedNewRecord =
    loadedTable.AsSpan(newRecordOffset, expected.Header.EntrySize).ToArray();
if (!loadedNewRecord.SequenceEqual(expectedNewRecord))
    throw new InvalidDataException($"In-memory command {newIndex} does not match.");

Console.WriteLine($"FFX process     : {game.Id}");
Console.WriteLine($"Table address   : 0x{commandFileAddress:X8}");
Console.WriteLine($"Loaded entries  : {loadedHeader.Header.RealEntryCount}");
Console.WriteLine($"Entry size      : 0x{loadedHeader.Header.EntrySize:X}");
Console.WriteLine($"New index       : {newIndex} (0x{newIndex:X3})");
Console.WriteLine($"Command ref     : 0x{(0x3000 | newIndex):X4}");
Console.WriteLine("Phase 2 game-load and in-memory slot checks passed.");
return 0;
