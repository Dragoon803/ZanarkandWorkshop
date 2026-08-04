using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record Map1Section(int Index, int Offset, int Length, byte[] Bytes);

public sealed class Map1Archive
{
    public const int SectionCount = 16;
    public string Path { get; }
    public IReadOnlyList<Map1Section> Sections { get; }

    private Map1Archive(string path, IReadOnlyList<Map1Section> sections)
    {
        Path = path;
        Sections = sections;
    }

    public Map1Section? FindSection(int index) => Sections.FirstOrDefault(section => section.Index == index);

    public static Map1Archive Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 4 || Encoding.ASCII.GetString(bytes, 0, 4) != Map1Header.Magic)
            throw new InvalidDataException($"'{path}' is not a valid MAP1 archive.");
        // Several unused field slots contain a 64-byte MAP1 placeholder with no section table.
        if (bytes.Length < 0x50)
            return new Map1Archive(System.IO.Path.GetFullPath(path), []);

        int[] offsets = Enumerable.Range(0, SectionCount)
            .Select(index => BitConverter.ToInt32(bytes, 0x10 + index * 4))
            .ToArray();
        var sections = new List<Map1Section>();
        for (int index = 0; index < offsets.Length; index++)
        {
            int offset = offsets[index];
            if (offset == 0) continue;
            if (offset < 0x50 || offset >= bytes.Length)
                throw new InvalidDataException($"MAP1 section {index} has invalid offset 0x{offset:X}.");
            int end = offsets.Where(candidate => candidate > offset).DefaultIfEmpty(bytes.Length).Min();
            if (end > bytes.Length || end <= offset)
                throw new InvalidDataException($"MAP1 section {index} has an invalid extent.");
            byte[] sectionBytes = new byte[end - offset];
            Array.Copy(bytes, offset, sectionBytes, 0, sectionBytes.Length);
            sections.Add(new Map1Section(index, offset, sectionBytes.Length, sectionBytes));
        }
        return new Map1Archive(System.IO.Path.GetFullPath(path), sections);
    }
}
