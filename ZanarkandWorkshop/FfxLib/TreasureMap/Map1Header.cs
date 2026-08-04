using System;
using System.IO;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record Map1Header(
    string Path,
    long FileLength,
    int HeaderLength,
    int DeclaredDataLength)
{
    public const string Magic = "MAP1";

    public static Map1Header Read(string path)
    {
        byte[] header = new byte[0x20];
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < header.Length || stream.Read(header, 0, header.Length) != header.Length)
            throw new InvalidDataException("mapout.vpa is too short to contain a MAP1 header.");
        if (Encoding.ASCII.GetString(header, 0, 4) != Magic)
            throw new InvalidDataException($"'{path}' does not begin with MAP1.");
        return new Map1Header(
            System.IO.Path.GetFullPath(path),
            stream.Length,
            BitConverter.ToInt32(header, 0x14),
            BitConverter.ToInt32(header, 0x18));
    }
}
