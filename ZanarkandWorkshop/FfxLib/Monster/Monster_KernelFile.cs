using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using Xe.BinaryMapper;
using static FFXProjectEditor.FfxLib.Monster.Monster_Structs;

namespace FFXProjectEditor.FfxLib.Monster
{
    /// <summary>
    /// One of the split battle/kernel/monster1.bin through monster3.bin files.
    /// The fixed records are the same stat sheets found in mXXX.bin, while all
    /// five text scripts use offsets into a shared text pool at the end.
    /// </summary>
    public sealed class Monster_KernelFile
    {
        public EntryListFile.FileHeader Header { get; private set; } = new();
        public List<Monster_StatSheet> Entries { get; } = new();

        public static Monster_KernelFile Read(byte[] byteFile)
        {
            EntryListFile packed = EntryListFile.Unpack(byteFile);
            if (packed.Header.EntrySize != 0x80)
                throw new InvalidDataException(
                    $"Unexpected monster kernel entry size 0x{packed.Header.EntrySize:X}; expected 0x80.");
            if (packed.SecondFile == null)
                throw new InvalidDataException("Monster kernel file has no shared text pool.");

            Monster_KernelFile file = new() { Header = packed.Header };
            using MemoryStream stream = new(packed.FirstFile);
            for (int index = 0; index < packed.Header.RealEntryCount; index++)
            {
                MonsterStatSheetStruct record = BinaryMapping.ReadObject<MonsterStatSheetStruct>(stream);
                Monster_StatSheet sheet = record.StatSheet;
                sheet.NameScriptBytes = ReadText(packed.SecondFile, record.NameTSInfo.Offset);
                sheet.SensorScriptBytes = ReadText(packed.SecondFile, record.SensorTSInfo.Offset);
                sheet.UnusedText1ScriptBytes = ReadText(packed.SecondFile, record.UnusedText1TSInfo.Offset);
                sheet.ScanScriptBytes = ReadText(packed.SecondFile, record.ScanTSInfo.Offset);
                sheet.UnusedText2ScriptBytes = ReadText(packed.SecondFile, record.UnusedText2TSInfo.Offset);
                sheet.NameScriptId = record.NameTSInfo.ScriptId;
                sheet.SensorScriptId = record.SensorTSInfo.ScriptId;
                sheet.UnusedText1ScriptId = record.UnusedText1TSInfo.ScriptId;
                sheet.ScanScriptId = record.ScanTSInfo.ScriptId;
                sheet.UnusedText2ScriptId = record.UnusedText2TSInfo.ScriptId;
                file.Entries.Add(sheet);
            }
            return file;
        }

        public Monster_StatSheet GetGlobalEntry(int monsterId)
        {
            int localIndex = monsterId - Header.PreviousFileCount;
            if (localIndex < 0 || localIndex >= Entries.Count)
                throw new ArgumentOutOfRangeException(nameof(monsterId),
                    $"Monster {monsterId} is outside this file's range " +
                    $"{Header.PreviousFileCount}-{Header.PreviousFileCount + Entries.Count - 1}.");
            return Entries[localIndex];
        }

        public byte[] Write()
        {
            using MemoryStream records = new();
            using MemoryStream textPool = new();

            foreach (Monster_StatSheet sheet in Entries)
            {
                MonsterStatSheetStruct record = new() { StatSheet = sheet };
                SetTextInfo(record.NameTSInfo, sheet.NameScriptId, sheet.NameScriptBytes, textPool);
                SetTextInfo(record.SensorTSInfo, sheet.SensorScriptId, sheet.SensorScriptBytes, textPool);
                SetTextInfo(record.UnusedText1TSInfo, sheet.UnusedText1ScriptId, sheet.UnusedText1ScriptBytes, textPool);
                SetTextInfo(record.ScanTSInfo, sheet.ScanScriptId, sheet.ScanScriptBytes, textPool);
                SetTextInfo(record.UnusedText2TSInfo, sheet.UnusedText2ScriptId, sheet.UnusedText2ScriptBytes, textPool);
                BinaryMapping.WriteObject(records, record);
            }

            Header.EntryCount = checked((short)(Header.PreviousFileCount + Entries.Count - 1));
            Header.EntrySize = 0x80;
            Header.EntryTableSize = checked((short)records.Length);
            Header.EntryTableFileOffset = 0x14;

            using MemoryStream output = new();
            BinaryMapping.WriteObject(output, Header);
            records.Position = 0;
            records.CopyTo(output);
            textPool.Position = 0;
            textPool.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] ReadText(byte[] textPool, ushort offset) =>
            FfxEncoding.GetScriptBytesFromTextFile(textPool, offset);

        private static void SetTextInfo(
            CommonStructs.TextScriptInfo info, ushort scriptId, byte[]? bytes, MemoryStream textPool)
        {
            if (textPool.Length > ushort.MaxValue)
                throw new InvalidDataException("Monster text pool exceeds the 16-bit offset limit.");

            info.Offset = (ushort)textPool.Length;
            info.ScriptId = scriptId;
            byte[] script = bytes ?? [];
            textPool.Write(script, 0, script.Length);
            textPool.WriteByte(0);
        }
    }
}
