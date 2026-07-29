using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.BattleFormation;

public static class BattleFormationParser
{
    private const int EnemySlotCount = 8;
    private const int PositionSize = 16;

    public static BattleFormationFile Read(string path) =>
        Read(File.ReadAllBytes(path), path);

    public static BattleFormationFile Read(byte[] bytes, string sourcePath = "")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        RequireRange(bytes, 0, 0x14, "battle header");

        int encounterOffset = ReadOffset(bytes, 0x0C, "encounter table");
        int positionHeaderOffset = ReadOffset(bytes, 0x10, "position header");
        RequireRange(bytes, encounterOffset, 28, "encounter record");
        RequireRange(bytes, positionHeaderOffset, 36, "position header");

        var enemyIds = new ushort[EnemySlotCount];
        for (int i = 0; i < EnemySlotCount; i++)
            enemyIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(encounterOffset + 12 + i * 2, 2));

        byte partyCount = bytes[positionHeaderOffset + 4];
        byte aeonCount = bytes[positionHeaderOffset + 5];
        byte monsterCount = bytes[positionHeaderOffset + 6];

        int partyStart = AddRelativeOffset(bytes, positionHeaderOffset, 16, "party-position table");
        int monsterStart = AddRelativeOffset(bytes, positionHeaderOffset, 32, "monster-position table");

        var positions = new List<FormationPosition>(
            partyCount * 2 + aeonCount + monsterCount * 2);
        int cursor = partyStart;
        ReadPositions(bytes, positions, FormationPositionKind.Party, partyCount, ref cursor);
        ReadPositions(bytes, positions, FormationPositionKind.PartySecondary, partyCount, ref cursor);
        ReadPositions(bytes, positions, FormationPositionKind.Aeon, aeonCount, ref cursor);

        cursor = monsterStart;
        ReadPositions(bytes, positions, FormationPositionKind.Monster, monsterCount, ref cursor);
        ReadPositions(bytes, positions, FormationPositionKind.MonsterSecondary, monsterCount, ref cursor);

        return new BattleFormationFile
        {
            OriginalBytes = (byte[])bytes.Clone(),
            SourcePath = sourcePath,
            EncounterOffset = encounterOffset,
            PositionHeaderOffset = positionHeaderOffset,
            EnemyIds = enemyIds,
            Positions = positions,
            PartyCount = partyCount,
            AeonCount = aeonCount,
            MonsterCount = monsterCount
        };
    }

    private static void ReadPositions(
        byte[] bytes,
        List<FormationPosition> destination,
        FormationPositionKind kind,
        int count,
        ref int cursor)
    {
        int byteCount = checked(count * PositionSize);
        RequireRange(bytes, cursor, byteCount, $"{kind} position table");
        for (int i = 0; i < count; i++)
        {
            int offset = cursor + i * PositionSize;
            destination.Add(new FormationPosition(
                kind,
                i,
                offset,
                ReadSingle(bytes, offset),
                ReadSingle(bytes, offset + 4),
                ReadSingle(bytes, offset + 8),
                ReadSingle(bytes, offset + 12)));
        }
        cursor += byteCount;
    }

    private static float ReadSingle(byte[] bytes, int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)));

    private static int ReadOffset(byte[] bytes, int at, string label)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
        if (value > int.MaxValue)
            throw new InvalidDataException($"{label} offset 0x{value:X8} is unsupported.");
        int offset = (int)value;
        if (offset < 0 || offset >= bytes.Length)
            throw new InvalidDataException(
                $"{label} offset 0x{offset:X} is outside the {bytes.Length}-byte file.");
        return offset;
    }

    private static int AddRelativeOffset(
        byte[] bytes, int baseOffset, int fieldOffset, string label)
    {
        RequireRange(bytes, baseOffset + fieldOffset, 4, $"{label} pointer");
        uint relative = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(baseOffset + fieldOffset, 4));
        long absolute = (long)baseOffset + relative;
        if (absolute < 0 || absolute > int.MaxValue || absolute >= bytes.Length)
            throw new InvalidDataException(
                $"{label} relative offset 0x{relative:X} resolves outside the file.");
        return (int)absolute;
    }

    private static void RequireRange(byte[] bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException(
                $"{label} requires 0x{length:X} bytes at 0x{offset:X}, outside the 0x{bytes.Length:X}-byte file.");
    }
}
