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

// Retail files may deliberately have fewer active enemy IDs than monster
// position pairs. Replacing an ID must remain a fixed-size edit.
ushort[] sparseIds = (ushort[])parsed.EnemyIds.Clone();
sparseIds[0] = 0x2222;
sparseIds[1] = ushort.MaxValue;
byte[] sparseReplacement = BattleFormationWriter.Write(parsed, sparseIds, parsed.Positions);
Assert(sparseReplacement.Length == original.Length,
    "A like-for-like replacement with spare retail positions resized the file");
BattleFormationFile sparseParsed = BattleFormationParser.Read(sparseReplacement, "sparse-replacement.bin");
Assert(sparseParsed.EnemyIds[0] == 0x2222 && sparseParsed.MonsterCount == parsed.MonsterCount,
    "A sparse like-for-like replacement changed the position count");

FormationPosition[] unsafePositions = parsed.Positions.ToArray();
unsafePositions[0] = unsafePositions[0] with
{
    X = float.NaN
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
Assert(rejectedUnsafeCoordinate, "Non-finite coordinates must be rejected");

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

bool rejectedEmptyFormation = false;
try
{
    _ = BattleFormationWriter.Write(parsed, expandedIds,
        parsed.Positions.Where(position => position.Kind is not FormationPositionKind.Monster and not FormationPositionKind.MonsterSecondary).ToArray());
}
catch (InvalidDataException ex) when (ex.Message.Contains("at least one monster", StringComparison.Ordinal))
{
    rejectedEmptyFormation = true;
}
Assert(rejectedEmptyFormation, "Zero-monster formations must be rejected");

bool rejectedEmptyEnemySlots = false;
try
{
    _ = BattleFormationWriter.Write(parsed,
        Enumerable.Repeat(ushort.MaxValue, 8).ToArray(), parsed.Positions);
}
catch (InvalidDataException ex) when (ex.Message.Contains("monster slot", StringComparison.Ordinal))
{
    rejectedEmptyEnemySlots = true;
}
Assert(rejectedEmptyEnemySlots,
    "An empty enemy party with leftover position records must be rejected");

if (args.Length == 1 && Directory.Exists(args[0]))
{
    int ok = 0;
    int excluded = 0;
    var failures = new List<string>();
    var wStats = new Dictionary<FormationPositionKind, (int Records, int NonZero)>();
    var nonZeroWExamples = new List<string>();
    foreach (string path in Directory.EnumerateFiles(args[0], "*.bin", SearchOption.AllDirectories))
    {
        BattleFormationFile file;
        try { file = BattleFormationParser.Read(path); }
        catch { excluded++; continue; }
        if (file.MonsterCount is < 1 or > 8 ||
            !file.EnemyIds.Any(id => id != ushort.MaxValue))
        {
            excluded++;
            continue;
        }
        try
        {
            byte[] roundTrip = BattleFormationWriter.WriteFixedSize(file, file.EnemyIds, file.Positions);
            Assert(file.OriginalBytes.SequenceEqual(roundTrip), "Round trip differs");
            if (file.CanResizeMonsterTables)
            {
                int targetCount = file.MonsterCount == 8 ? 7 : file.MonsterCount + 1;
                FormationPosition[] resizedPositions = ResizeMonsterPositions(file.Positions, targetCount);
                ushort[] resizedIds = (ushort[])file.EnemyIds.Clone();
                for (int i = 0; i < resizedIds.Length; i++)
                    resizedIds[i] = i < targetCount
                        ? (resizedIds.FirstOrDefault(id => id != ushort.MaxValue) is ushort id && id != 0 ? id : (ushort)1)
                        : ushort.MaxValue;
                byte[] resized = BattleFormationWriter.Write(file, resizedIds, resizedPositions);
                BattleFormationFile resizedFile = BattleFormationParser.Read(resized, path);
                Assert(resizedFile.MonsterCount == targetCount, "Resized monster count differs");
            }
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
    Console.WriteLine($"Corpus scan: {ok} editable formations passed, {excluded} non-formations excluded, {failures.Count} failed.");
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

static FormationPosition[] ResizeMonsterPositions(
    IReadOnlyList<FormationPosition> source, int targetCount)
{
    FormationPosition[] monsters = source.Where(position => position.Kind == FormationPositionKind.Monster).ToArray();
    FormationPosition[] run = source.Where(position => position.Kind == FormationPositionKind.MonsterSecondary).ToArray();
    var result = source.Where(position => position.Kind is not FormationPositionKind.Monster and not FormationPositionKind.MonsterSecondary).ToList();
    FormationPosition monsterTemplate = monsters.FirstOrDefault() ?? new FormationPosition(FormationPositionKind.Monster, 0, -1, 0, 0, 60, 0);
    FormationPosition runTemplate = run.FirstOrDefault() ?? new FormationPosition(FormationPositionKind.MonsterSecondary, 0, -1, 90, 0, -60, 0);
    for (int i = 0; i < targetCount; i++)
        result.Add((i < monsters.Length ? monsters[i] : monsterTemplate) with
        { Kind = FormationPositionKind.Monster, Index = i, FileOffset = i < monsters.Length ? monsters[i].FileOffset : -1 });
    for (int i = 0; i < targetCount; i++)
        result.Add((i < run.Length ? run[i] : runTemplate) with
        { Kind = FormationPositionKind.MonsterSecondary, Index = i, FileOffset = i < run.Length ? run[i].FileOffset : -1 });
    return result.ToArray();
}
