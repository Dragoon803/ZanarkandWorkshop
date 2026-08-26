using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record GuideMapVertex(float X, float Y, float Z);
public sealed record GuideMapTriangle(ushort A, ushort B, ushort C);

public sealed record GuideMapModel(
    int SceneIndex,
    int Flags,
    IReadOnlyList<GuideMapVertex> Vertices,
    IReadOnlyList<GuideMapTriangle> Triangles,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    Matrix4x4 LocalTransform);

public sealed record GuideMapGeometry(IReadOnlyList<GuideMapModel> Models)
{
    public static GuideMapGeometry Read(Map1Archive archive)
    {
        Map1Section? section = archive.FindSection(11);
        if (section == null) return new GuideMapGeometry([]);
        byte[] bytes = section.Bytes;
        var models = new List<GuideMapModel>();
        int cursor = 0;
        while (cursor + 16 <= bytes.Length)
        {
            string tag = Encoding.ASCII.GetString(bytes, cursor, 4);
            int blocks = BitConverter.ToInt32(bytes, cursor + 4);
            int sceneIndex = BitConverter.ToUInt16(bytes, cursor + 10);
            if (blocks < 0 || blocks > (bytes.Length - cursor - 16) / 16)
                throw new InvalidDataException($"Guide-map chunk '{tag}' has an invalid block count.");
            int payloadOffset = cursor + 16;
            int payloadLength = checked(blocks * 16);
            if (tag == "YNGM")
                models.Add(ReadModel(bytes, payloadOffset, payloadLength, sceneIndex));
            cursor = checked(payloadOffset + payloadLength);
            if (tag == "YNED") break;
        }
        return new GuideMapGeometry(models);
    }

    private static GuideMapModel ReadModel(byte[] bytes, int offset, int length, int sceneIndex)
    {
        Require(length >= 8, "YNGM payload is too short.");
        int flags = BitConverter.ToInt32(bytes, offset);
        int blobLength = BitConverter.ToInt32(bytes, offset + 4);
        // Five retail fields omit the final eight bytes of the second trailing matrix.
        // Geometry, bounds, and the local transform are complete by fixed offset 0xE0.
        Require(blobLength >= 0x20 && 8 + blobLength + 0xE0 <= length, "YNGM model blob has an invalid length.");
        int blob = offset + 8;
        int vertexOffset = BitConverter.ToInt32(bytes, blob + 8);
        int vertexCount = BitConverter.ToUInt16(bytes, blob + 0x12);
        Require(vertexOffset >= 0x20 && vertexOffset + vertexCount * 6 <= blobLength,
            "YNGM vertex table is outside the model blob.");

        int fixedData = blob + blobLength;
        int transformOffset = 0xA0;
        Matrix4x4 transform = ReadMatrix(bytes, fixedData + transformOffset);
        if (!IsScaleMatrix(transform))
        {
            // A small retail variant places the bounds/transform block 0x10 bytes earlier.
            transformOffset = 0x90;
            transform = ReadMatrix(bytes, fixedData + transformOffset);
        }
        Vector3 boundsMin = ReadVector3(bytes, fixedData + transformOffset - 0x30);
        Vector3 boundsMax = ReadVector3(bytes, fixedData + transformOffset - 0x20);
        float encodedScale = transform.M11;
        Require(IsScaleMatrix(transform), "YNGM model has an invalid vertex scale.");

        var vertices = new GuideMapVertex[vertexCount];
        for (int index = 0; index < vertexCount; index++)
        {
            int position = blob + vertexOffset + index * 6;
            // The guide renderer's short conversion uses tenths before applying the model scale.
            vertices[index] = new GuideMapVertex(
                BitConverter.ToInt16(bytes, position) * encodedScale / 10f,
                BitConverter.ToInt16(bytes, position + 2) * transform.M22 / 10f,
                BitConverter.ToInt16(bytes, position + 4) * transform.M33 / 10f);
        }

        var triangles = new List<GuideMapTriangle>();
        int primitive = blob + 0x20;
        int primitiveEnd = blob + vertexOffset;
        while (primitive + 16 <= primitiveEnd)
        {
            if (BitConverter.ToUInt16(bytes, primitive) == 0xFFFF) break;
            int primitiveType = bytes[primitive + 1];
            int count = BitConverter.ToUInt16(bytes, primitive + 2);
            if (primitiveType != 0)
                throw new InvalidDataException($"Unsupported YNGM primitive type {primitiveType}.");
            int records = primitive + 16;
            Require(records + count * 20 <= primitiveEnd, "YNGM triangle records exceed the primitive table.");
            for (int index = 0; index < count; index++)
            {
                int record = records + index * 20;
                ushort a = BitConverter.ToUInt16(bytes, record + 12);
                ushort b = BitConverter.ToUInt16(bytes, record + 14);
                ushort c = BitConverter.ToUInt16(bytes, record + 16);
                Require(a < vertexCount && b < vertexCount && c < vertexCount,
                    "YNGM triangle references a missing vertex.");
                triangles.Add(new GuideMapTriangle(a, b, c));
            }
            primitive = records + count * 20;
            primitive = (primitive + 15) & ~15;
        }

        return new GuideMapModel(sceneIndex, flags, vertices, triangles, boundsMin, boundsMax, transform);
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset) =>
        new(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4),
            BitConverter.ToSingle(bytes, offset + 8));

    private static Matrix4x4 ReadMatrix(byte[] bytes, int offset) => new(
        BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8), BitConverter.ToSingle(bytes, offset + 12),
        BitConverter.ToSingle(bytes, offset + 16), BitConverter.ToSingle(bytes, offset + 20), BitConverter.ToSingle(bytes, offset + 24), BitConverter.ToSingle(bytes, offset + 28),
        BitConverter.ToSingle(bytes, offset + 32), BitConverter.ToSingle(bytes, offset + 36), BitConverter.ToSingle(bytes, offset + 40), BitConverter.ToSingle(bytes, offset + 44),
        BitConverter.ToSingle(bytes, offset + 48), BitConverter.ToSingle(bytes, offset + 52), BitConverter.ToSingle(bytes, offset + 56), BitConverter.ToSingle(bytes, offset + 60));

    private static bool IsScaleMatrix(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && matrix.M11 != 0 &&
        float.IsFinite(matrix.M22) && matrix.M22 != 0 &&
        float.IsFinite(matrix.M33) && matrix.M33 != 0 &&
        Math.Abs(matrix.M44 - 1f) < 0.001f;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
