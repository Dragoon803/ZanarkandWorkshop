using FFXProjectEditor.FfxLib.BattleFormation;
using System.Buffers.Binary;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void WriteUInt32(byte[] bytes, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

static void WriteSingle(byte[] bytes, int offset, float value) =>
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

static byte[] MakeSyntheticFile()
{
    var bytes = new byte[0x180];
    const int encounter = 0x40;
    const int header = 0x80;
    const int partyStart = 0xB0;
    const int monsterStart = 0x100;

    WriteUInt32(bytes, 0x0C, encounter);
    WriteUInt32(bytes, 0x10, header);
    for (int i = 0; i < 8; i++)
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(encounter + 12 + i * 2, 2), (ushort)(0x1000 + i));

    bytes[header + 4] = 1;
    bytes[header + 5] = 1;
    bytes[header + 6] = 1;
    WriteUInt32(bytes, header + 16, partyStart - header);
    WriteUInt32(bytes, header + 32, monsterStart - header);
    WriteUInt32(bytes, header + 36, monsterStart + 16 - header);
    WriteUInt32(bytes, header + 44, monsterStart + 32 - header);

    int[] offsets = { partyStart, partyStart + 16, partyStart + 32, monsterStart, monsterStart + 16 };
    for (int i = 0; i < offsets.Length; i++)
    {
        WriteSingle(bytes, offsets[i], i + 0.25f);
        WriteSingle(bytes, offsets[i] + 4, i + 1.25f);
        WriteSingle(bytes, offsets[i] + 8, i + 2.25f);
        WriteSingle(bytes, offsets[i] + 12, i + 3.25f);
    }
    return bytes;
}

byte[] original = MakeSyntheticFile();
BattleFormationFile parsed = BattleFormationParser.Read(original, "synthetic.bin");
Assert(parsed.EnemyIds.Length == 8, "Enemy slot count");
Assert(parsed.Positions.Count == 5, "Position count");
Assert(parsed.Positions[0].X == 0.25f, "Float parsing");

byte[] identical = BattleFormationWriter.WriteFixedSize(parsed, parsed.EnemyIds, parsed.Positions);
Assert(original.SequenceEqual(identical), "Unchanged round trip must be byte-identical");

ushort[] changedIds = (ushort[])parsed.EnemyIds.Clone();
changedIds[7] = ushort.MaxValue;
FormationPosition[] changedPositions = parsed.Positions.ToArray();
changedPositions[3] = changedPositions[3] with { X = -123.5f };
byte[] changed = BattleFormationWriter.WriteFixedSize(parsed, changedIds, changedPositions);
BattleFormationFile reparsed = BattleFormationParser.Read(changed, "changed.bin");
Assert(reparsed.EnemyIds[7] == ushort.MaxValue, "Empty enemy slot");
Assert(reparsed.Positions[3].X == -123.5f, "Edited position");
Assert(changed.Length == original.Length, "Fixed-size writer changed file length");

FormationPosition[] unsafePositions = parsed.Positions.ToArray();
unsafePositions[0] = unsafePositions[0] with
{
    X = BattleFormationWriter.AbsoluteCoordinateLimit + 1
};
bool rejectedUnsafeCoordinate = false;
try
{
    _ = BattleFormationWriter.WriteFixedSize(parsed, parsed.EnemyIds, unsafePositions);
}
catch (InvalidDataException)
{
    rejectedUnsafeCoordinate = true;
}
Assert(rejectedUnsafeCoordinate, "Unsafe coordinates must be rejected");

var expandedPositions = parsed.Positions.ToList();
expandedPositions.Add(new FormationPosition(
    FormationPositionKind.Monster, 1, -1, 25, 0, 55, 0));
expandedPositions.Add(new FormationPosition(
    FormationPositionKind.MonsterSecondary, 1, -1, 90, 0, -60, 0));
ushort[] expandedIds = (ushort[])parsed.EnemyIds.Clone();
expandedIds[1] = 0x1010;
byte[] expanded = BattleFormationWriter.Write(parsed, expandedIds, expandedPositions);
BattleFormationFile expandedParsed = BattleFormationParser.Read(expanded, "expanded.bin");
Assert(expandedParsed.MonsterCount == 2, "Expanded monster count");
Assert(expanded.Length == original.Length + 32, "Expanded position block size");
Assert(expandedParsed.Positions.Single(position =>
    position.Kind == FormationPositionKind.Monster && position.Index == 1).X == 25,
    "Expanded monster position");

if (args.Length == 1 && Directory.Exists(args[0]))
{
    int ok = 0;
    var failures = new List<string>();
    var wStats = new Dictionary<FormationPositionKind, (int Records, int NonZero)>();
    var nonZeroWExamples = new List<string>();
    foreach (string path in Directory.EnumerateFiles(args[0], "*.bin", SearchOption.AllDirectories))
    {
        try
        {
            BattleFormationFile file = BattleFormationParser.Read(path);
            byte[] roundTrip = BattleFormationWriter.WriteFixedSize(file, file.EnemyIds, file.Positions);
            Assert(file.OriginalBytes.SequenceEqual(roundTrip), "Round trip differs");
            foreach (FormationPosition position in file.Positions)
            {
                wStats.TryGetValue(position.Kind, out (int Records, int NonZero) stats);
                stats.Records++;
                if (position.W != 0)
                {
                    stats.NonZero++;
                    if (nonZeroWExamples.Count < 10)
                        nonZeroWExamples.Add(
                            $"{Path.GetFileName(path)} {position.Kind} {position.Index + 1}: W={position.W}");
                }
                wStats[position.Kind] = stats;
            }
            ok++;
        }
        catch (Exception ex)
        {
            failures.Add($"{path}: {ex.Message}");
        }
    }
    Console.WriteLine($"Corpus scan: {ok} passed, {failures.Count} failed.");
    foreach (var pair in wStats.OrderBy(pair => pair.Key))
        Console.WriteLine($"{pair.Key}: {pair.Value.NonZero}/{pair.Value.Records} nonzero W values.");
    foreach (string example in nonZeroWExamples)
        Console.WriteLine(example);
    foreach (string failure in failures.Take(20))
        Console.WriteLine(failure);
    if (failures.Count > 0) Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("Synthetic battle-formation smoke tests passed.");
}
