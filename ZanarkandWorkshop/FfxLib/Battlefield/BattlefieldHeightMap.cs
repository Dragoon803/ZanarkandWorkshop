using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.FfxLib.TreasureMap;

namespace FFXProjectEditor.FfxLib.Battlefield;

public readonly record struct BattlefieldVertex(float X, float Y, float Z);
public readonly record struct BattlefieldTriangle(ushort A, ushort B, ushort C, uint Attributes);

public sealed class BattlefieldHeightMap
{
    public required IReadOnlyList<BattlefieldVertex> Vertices { get; init; }
    public required IReadOnlyList<BattlefieldTriangle> Triangles { get; init; }
    public required float PackedScale { get; init; }

    public static BattlefieldHeightMap Read(string mapPath) => Read(Map1Archive.Read(mapPath));

    public static bool TryRead(string mapPath, out BattlefieldHeightMap? heightMap)
    {
        Map1Archive archive = Map1Archive.Read(mapPath);
        if (archive.FindSection(2) is null)
        {
            heightMap = null;
            return false;
        }
        heightMap = Read(archive);
        return true;
    }

    public static BattlefieldHeightMap Read(Map1Archive archive)
    {
        Map1Section section = archive.FindSection(2)
            ?? throw new InvalidDataException("The battlefield MAP1 archive has no height-map section.");
        ReadOnlySpan<byte> bytes = section.Bytes;
        Require(bytes, 0, 0x20, "height-map header");

        int vertexCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(0x0A, 2));
        float scale = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0x0C, 4))) / 10f;
        if (!float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("The battlefield height map has an invalid coordinate scale.");

        int vertexOffset = ReadRelativeOffset(bytes, 0x18, "vertex table");
        int triangleHeaderOffset = ReadRelativeOffset(bytes, 0x1C, "triangle header");
        Require(bytes, triangleHeaderOffset, 0x10, "triangle header");
        int triangleCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(triangleHeaderOffset + 8, 2));
        int triangleOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(triangleHeaderOffset + 0x0C, 4)));

        Require(bytes, vertexOffset, checked(vertexCount * 8), "vertex table");
        Require(bytes, triangleOffset, checked(triangleCount * 0x10), "triangle table");

        var vertices = new BattlefieldVertex[vertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            int offset = vertexOffset + i * 8;
            float x = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, 2)) / scale;
            float y = -BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset + 2, 2)) / scale;
            float z = -BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset + 4, 2)) / scale;
            vertices[i] = new BattlefieldVertex(x, y, z);
        }

        var triangles = new BattlefieldTriangle[triangleCount];
        for (int i = 0; i < triangles.Length; i++)
        {
            int offset = triangleOffset + i * 0x10;
            ushort a = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
            ushort b = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 2, 2));
            ushort c = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 4, 2));
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
                throw new InvalidDataException($"Battlefield triangle {i} references an invalid vertex.");
            uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 0x0C, 4));
            triangles[i] = new BattlefieldTriangle(a, b, c, attributes);
        }

        return new BattlefieldHeightMap
        {
            Vertices = vertices,
            Triangles = triangles,
            PackedScale = scale
        };
    }

    private static int ReadRelativeOffset(ReadOnlySpan<byte> bytes, int at, string label)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at, 4));
        if (value > int.MaxValue || value >= bytes.Length)
            throw new InvalidDataException($"The battlefield {label} offset is outside its MAP1 section.");
        return (int)value;
    }

    private static void Require(ReadOnlySpan<byte> bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException($"The battlefield {label} is truncated.");
    }
}
