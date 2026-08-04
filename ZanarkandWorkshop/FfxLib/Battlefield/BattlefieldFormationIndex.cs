using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Battlefield;

public sealed record BattlefieldFormationAssignment(
    string FormationName,
    ushort BattlefieldId,
    ushort FormationFileId,
    byte Weight);

public sealed class BattlefieldFormationIndex
{
    private readonly IReadOnlyDictionary<string, BattlefieldFormationAssignment> _assignments;
    private readonly IReadOnlyList<BattlefieldFormationAssignment> _assignmentList;

    public IReadOnlyList<BattlefieldFormationAssignment> Assignments => _assignmentList;

    private BattlefieldFormationIndex(
        IReadOnlyDictionary<string, BattlefieldFormationAssignment> assignments)
    {
        _assignments = assignments;
        _assignmentList = assignments.Values.ToArray();
    }

    public static BattlefieldFormationIndex ReadProject(string projectRoot) =>
        Read(Path.Combine(projectRoot, "jppc", "battle", "kernel", "btl.bin"));

    public static BattlefieldFormationIndex Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Require(bytes, 0, 0x10, "battle-list header");
        int listOffset = ReadOffset(bytes, 0x04, "list table");
        int dataOffset = ReadOffset(bytes, 0x08, "pool data");
        if (listOffset > dataOffset)
            throw new InvalidDataException("The battle-list table begins after its pool data.");

        var assignments = new Dictionary<string, BattlefieldFormationAssignment>(
            StringComparer.OrdinalIgnoreCase);
        for (int offset = listOffset; offset < dataOffset; offset += 0x0E)
        {
            Require(bytes, offset, 0x0E, "battle-list record");
            int poolOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2, 2));
            ushort formationFileId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2));
            string name = Encoding.ASCII.GetString(bytes, offset + 6, 6).TrimEnd('\0');
            if (name.Length == 0) continue;

            int cursor = checked(dataOffset + poolOffset);
            Require(bytes, cursor, 2, $"{name} pool header");
            cursor++; // Unknown leading byte used by the retail table.
            int poolCount = bytes[cursor++];
            for (int pool = 0; pool < poolCount; pool++)
            {
                Require(bytes, cursor, 5, $"{name} pool {pool + 1}");
                int encounterCount = bytes[cursor];
                ushort battlefieldId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 1, 2));
                cursor += 5;
                for (int encounter = 0; encounter < encounterCount; encounter++)
                {
                    Require(bytes, cursor, 2, $"{name} encounter {encounter + 1}");
                    byte encounterId = bytes[cursor];
                    byte weight = bytes[cursor + 1];
                    cursor += 2;
                    // Retail formation folders print this byte as decimal (0x0A -> "10").
                    string formationName = $"{name}_{encounterId:D2}";
                    var assignment = new BattlefieldFormationAssignment(
                        formationName, battlefieldId, formationFileId++, weight);
                    if (!assignments.TryAdd(formationName, assignment) &&
                        assignments[formationName].BattlefieldId != battlefieldId)
                    {
                        throw new InvalidDataException(
                            $"Formation {formationName} is assigned to conflicting battlefield IDs.");
                    }
                }
            }
        }
        return new BattlefieldFormationIndex(assignments);
    }

    public bool TryResolve(string formationName, out BattlefieldFormationAssignment? assignment) =>
        _assignments.TryGetValue(Path.GetFileNameWithoutExtension(formationName), out assignment);

    public BattlefieldAsset? ResolveAsset(
        string formationName,
        IReadOnlyList<BattlefieldAsset> assets)
    {
        if (!TryResolve(formationName, out BattlefieldFormationAssignment? assignment)) return null;
        return assets.FirstOrDefault(asset => asset.Id == assignment!.BattlefieldId);
    }

    private static int ReadOffset(byte[] bytes, int at, string label)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
        if (value > int.MaxValue || value >= bytes.Length)
            throw new InvalidDataException($"The {label} offset is outside btl.bin.");
        return (int)value;
    }

    private static void Require(byte[] bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"The {label} is truncated.");
    }
}
