using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record EventPackageChunk(int Index, int Offset, byte[] Bytes);

public sealed class EventPackage
{
    public const string Magic = "EV01";

    public string Path { get; }
    public IReadOnlyList<EventPackageChunk> Chunks { get; }
    public byte[] AtelBytes => Chunks.Count == 0 ? [] : Chunks[0].Bytes;

    private EventPackage(string path, IReadOnlyList<EventPackageChunk> chunks)
    {
        Path = path;
        Chunks = chunks;
    }

    public static EventPackage Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != Magic)
            throw new InvalidDataException($"'{path}' is not an EV01 event package.");

        var offsets = new List<int>();
        for (int cursor = 4; cursor <= bytes.Length - 4; cursor += 4)
        {
            uint raw = BitConverter.ToUInt32(bytes, cursor);
            if (raw == uint.MaxValue)
                break;
            int offset = checked((int)raw);
            if (offset != 0 && (offset < 0x10 || offset > bytes.Length))
                throw new InvalidDataException(
                    $"Event package chunk offset 0x{offset:X} is outside the file.");
            offsets.Add(offset);
            if (offset == bytes.Length)
                break;
        }
        if (offsets.Count < 2)
            throw new InvalidDataException("Event package does not contain a complete chunk table.");

        var chunks = new List<EventPackageChunk>();
        for (int index = 0; index < offsets.Count - 1; index++)
        {
            int start = offsets[index];
            if (start == 0)
            {
                chunks.Add(new EventPackageChunk(index, 0, []));
                continue;
            }
            int end = offsets.Skip(index + 1).FirstOrDefault(candidate => candidate >= start);
            if (end == 0) end = bytes.Length;
            if (end < start || end > bytes.Length)
                throw new InvalidDataException($"Event package chunk {index} has invalid bounds.");
            chunks.Add(new EventPackageChunk(index, start, bytes.AsSpan(start, end - start).ToArray()));
        }
        return new EventPackage(System.IO.Path.GetFullPath(path), chunks);
    }
}
