using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public enum TreasureKind : byte
{
    Gil = 0x00,
    Item = 0x02,
    Equipment = 0x05,
    KeyItem = 0x0A
}

public sealed record TreasureRecord(
    int Id,
    int FileOffset,
    byte RawKind,
    byte Quantity,
    ushort Type)
{
    public TreasureKind? Kind => Enum.IsDefined(typeof(TreasureKind), RawKind)
        ? (TreasureKind)RawKind
        : null;

    public int GilAmount => RawKind == (byte)TreasureKind.Gil ? Quantity * 100 : 0;
}

public sealed class TreasureCatalog
{
    public const int HeaderLength = 0x14;
    public const int RecordLength = 4;

    public string Path { get; }
    public IReadOnlyList<TreasureRecord> Records { get; }

    private TreasureCatalog(string path, IReadOnlyList<TreasureRecord> records)
    {
        Path = path;
        Records = records;
    }

    public static TreasureCatalog Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A takara.bin path is required.", nameof(path));
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderLength)
            throw new InvalidDataException("takara.bin is shorter than its 0x14-byte header.");
        int dataLength = bytes.Length - HeaderLength;
        if (dataLength % RecordLength != 0)
            throw new InvalidDataException(
                $"takara.bin has an invalid data length: 0x{dataLength:X} is not divisible by four.");

        var records = new List<TreasureRecord>(dataLength / RecordLength);
        for (int id = 0; id < dataLength / RecordLength; id++)
        {
            int offset = HeaderLength + id * RecordLength;
            records.Add(new TreasureRecord(
                id,
                offset,
                bytes[offset],
                bytes[offset + 1],
                (ushort)(bytes[offset + 2] | bytes[offset + 3] << 8)));
        }
        return new TreasureCatalog(System.IO.Path.GetFullPath(path), records);
    }
}
