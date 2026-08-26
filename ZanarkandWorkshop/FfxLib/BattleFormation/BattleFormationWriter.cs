using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.BattleFormation;

public static class BattleFormationWriter
{
    private const int PositionSize = 16;

    public static byte[] Write(
        BattleFormationFile source,
        IReadOnlyList<ushort> enemyIds,
        IReadOnlyList<FormationPosition> positions)
    {
        ValidateEnemyParty(enemyIds);
        int newMonsterCount = positions.Count(position =>
            position.Kind == FormationPositionKind.Monster);
        int newRunCount = positions.Count(position =>
            position.Kind == FormationPositionKind.MonsterSecondary);
        if (newMonsterCount != newRunCount)
            throw new InvalidDataException(
                "Every monster position must have a matching run-away position.");
        if (newMonsterCount < 1)
            throw new InvalidDataException("A formation must contain at least one monster.");
        if (newMonsterCount > 8)
            throw new InvalidDataException("A formation cannot contain more than eight monsters.");
        var expectedOffsets = source.Positions.Select(position => position.FileOffset).ToHashSet();
        if (newMonsterCount == source.MonsterCount &&
            positions.All(position => expectedOffsets.Contains(position.FileOffset)))
            return WriteFixedSize(source, enemyIds, positions);
        if (!source.CanResizeMonsterTables)
            throw new InvalidDataException(
                "This formation uses a fixed-size retail layout. Monsters can be replaced, but slots cannot be added or removed.");
        return WriteResizedMonsterTables(source, enemyIds, positions, newMonsterCount);
    }

    public static byte[] WriteFixedSize(
        BattleFormationFile source,
        IReadOnlyList<ushort> enemyIds,
        IReadOnlyList<FormationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateEnemyParty(enemyIds);
        if (enemyIds.Count != 8)
            throw new InvalidDataException("A battle formation must contain exactly eight enemy slots.");
        if (positions.Count != source.Positions.Count)
            throw new InvalidDataException("Fixed-size saving cannot add or remove position records.");

        var expectedOffsets = source.Positions.Select(position => position.FileOffset).ToHashSet();
        if (positions.Any(position => !expectedOffsets.Contains(position.FileOffset)))
            throw new InvalidDataException("A position record no longer maps to its original file offset.");

        byte[] output = (byte[])source.OriginalBytes.Clone();
        for (int i = 0; i < enemyIds.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(
                output.AsSpan(source.EncounterOffset + 12 + i * 2, 2), enemyIds[i]);

        foreach (FormationPosition position in positions)
        {
            ValidateCoordinate(position.X, position, "X");
            ValidateCoordinate(position.Y, position, "Y");
            ValidateCoordinate(position.Z, position, "Z");
            ValidateCoordinate(position.W, position, "W");
            WriteSingle(output, position.FileOffset, position.X);
            WriteSingle(output, position.FileOffset + 4, position.Y);
            WriteSingle(output, position.FileOffset + 8, position.Z);
            WriteSingle(output, position.FileOffset + 12, position.W);
        }
        return output;
    }

    private static byte[] WriteResizedMonsterTables(
        BattleFormationFile source,
        IReadOnlyList<ushort> enemyIds,
        IReadOnlyList<FormationPosition> positions,
        int newMonsterCount)
    {
        if (enemyIds.Count != 8)
            throw new InvalidDataException("A battle formation must contain exactly eight enemy slots.");

        int header = source.PositionHeaderOffset;
        byte[] original = source.OriginalBytes;
        int enemyStart = AddRelativeOffset(original, header, 0x20);
        int enemyRunStart = AddRelativeOffset(original, header, 0x24);
        int chunkEnd = AddRelativeOffset(original, header, 0x2C);
        int oldMonsterBytes = checked(source.MonsterCount * PositionSize);
        if (enemyRunStart != enemyStart + oldMonsterBytes ||
            chunkEnd < enemyRunStart + oldMonsterBytes)
            throw new InvalidDataException(
                "This file’s monster position tables do not match the supported retail layout. " +
                "Restore this formation from a clean master copy, then try adding or removing the monster again.");

        int oldTablesEnd = enemyRunStart + oldMonsterBytes;
        int newMonsterBytes = checked(newMonsterCount * PositionSize);
        int delta = checked((newMonsterCount - source.MonsterCount) * PositionSize * 2);
        byte[] output = new byte[checked(original.Length + delta)];

        original.AsSpan(0, enemyStart).CopyTo(output);
        int newRunStart = enemyStart + newMonsterBytes;
        int newTablesEnd = newRunStart + newMonsterBytes;
        original.AsSpan(oldTablesEnd).CopyTo(output.AsSpan(newTablesEnd));

        output[header + 6] = checked((byte)newMonsterCount);
        WriteUInt32(output, header + 0x24, checked((uint)(newRunStart - header)));
        WriteUInt32(output, header + 0x2C, checked((uint)(chunkEnd + delta - header)));
        if (BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0x20, 4)) == original.Length)
            WriteUInt32(output, 0x20, checked((uint)output.Length));

        for (int i = 0; i < enemyIds.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(
                output.AsSpan(source.EncounterOffset + 12 + i * 2, 2), enemyIds[i]);

        foreach (FormationPosition position in positions)
        {
            ValidateCoordinate(position.X, position, "X");
            ValidateCoordinate(position.Y, position, "Y");
            ValidateCoordinate(position.Z, position, "Z");
            ValidateCoordinate(position.W, position, "W");
            int offset = position.Kind switch
            {
                FormationPositionKind.Monster =>
                    enemyStart + position.Index * PositionSize,
                FormationPositionKind.MonsterSecondary =>
                    newRunStart + position.Index * PositionSize,
                _ => position.FileOffset
            };
            WritePosition(output, offset, position);
        }
        return output;
    }

    private static int AddRelativeOffset(byte[] bytes, int header, int field)
    {
        uint relative = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(header + field, 4));
        long absolute = (long)header + relative;
        if (absolute < 0 || absolute > bytes.Length)
            throw new InvalidDataException("A position-table offset resolves outside the file.");
        return (int)absolute;
    }

    private static void WritePosition(byte[] output, int offset, FormationPosition position)
    {
        WriteSingle(output, offset, position.X);
        WriteSingle(output, offset + 4, position.Y);
        WriteSingle(output, offset + 8, position.Z);
        WriteSingle(output, offset + 12, position.W);
    }

    private static void WriteUInt32(byte[] output, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset, 4), value);

    private static void ValidateCoordinate(
        float value, FormationPosition position, string coordinate)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException(
                $"{position.Kind} {position.Index + 1} has a non-finite {coordinate} coordinate.");
    }

    private static void WriteSingle(byte[] output, int offset, float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException("Formation coordinates must be finite numbers.");
        BinaryPrimitives.WriteInt32LittleEndian(
            output.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    }

    private static void ValidateEnemyParty(IReadOnlyList<ushort> enemyIds)
    {
        if (enemyIds.Count(id => id != ushort.MaxValue) == 0)
            throw new InvalidDataException(
                "At least one monster slot must be filled before saving.");
    }
}
