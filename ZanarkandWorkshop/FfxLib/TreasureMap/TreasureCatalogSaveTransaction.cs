using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public static class TreasureCatalogWriter
{
    public static byte[] Write(TreasureCatalog source, IEnumerable<TreasureRecord> records)
    {
        byte[] output = File.ReadAllBytes(source.Path);
        TreasureRecord[] edited = records.OrderBy(record => record.Id).ToArray();
        if (edited.Length != source.Records.Count)
            throw new InvalidDataException("The treasure record count cannot be changed.");
        for (int id = 0; id < edited.Length; id++)
        {
            TreasureRecord record = edited[id];
            if (record.Id != id || record.FileOffset != TreasureCatalog.HeaderLength + id * 4)
                throw new InvalidDataException($"Treasure record {id} has an invalid identity or offset.");
            int offset = record.FileOffset;
            output[offset] = record.RawKind;
            output[offset + 1] = record.Quantity;
            output[offset + 2] = (byte)record.Type;
            output[offset + 3] = (byte)(record.Type >> 8);
        }
        TreasureCatalogValidator.ValidateOutput(source, output);
        return output;
    }
}

public static class TreasureCatalogValidator
{
    public static void ValidateOutput(TreasureCatalog source, byte[] output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        byte[] original = File.ReadAllBytes(source.Path);
        if (output.Length != original.Length)
            throw new InvalidDataException("The staged treasure catalog changed file length.");
        if (!output.AsSpan(0, TreasureCatalog.HeaderLength).SequenceEqual(
                original.AsSpan(0, TreasureCatalog.HeaderLength)))
            throw new InvalidDataException("The staged treasure catalog changed the takara.bin header.");
        if (output.Length != TreasureCatalog.HeaderLength + source.Records.Count * TreasureCatalog.RecordLength)
            throw new InvalidDataException("The staged treasure catalog does not match the source record count.");
    }
}

public static class TreasureCatalogSaveTransaction
{
    public static TreasureCatalog Save(TreasureCatalog source, byte[] output)
    {
        TreasureCatalogValidator.ValidateOutput(source, output);
        string temporary = source.Path + ".zwtmp";
        try
        {
            File.WriteAllBytes(temporary, output);
            TreasureCatalog verified = TreasureCatalog.Read(temporary);
            if (verified.Records.Count != source.Records.Count)
                throw new InvalidDataException("The staged treasure catalog changed record count.");
            if (!File.ReadAllBytes(temporary).SequenceEqual(output))
                throw new InvalidDataException("The staged treasure catalog did not verify byte-for-byte.");
            byte[] original = File.ReadAllBytes(source.Path);
            try { File.Move(temporary, source.Path, true); }
            catch (Exception saveError)
            {
                try { File.WriteAllBytes(source.Path, original); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Treasure saving and automatic rollback both failed. Use Recovery to restore takara.bin.", saveError, rollbackError);
                }
                throw new IOException("Treasure saving failed. The project file was restored.", saveError);
            }
            return TreasureCatalog.Read(source.Path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
