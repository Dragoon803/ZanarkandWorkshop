using FFXProjectEditor.FfxLib.Atel;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: AtelSmoke <battle/mon directory>");
    return 2;
}

int parsed = 0;
int recoveredHeaders = 0;
var failures = new List<string>();
foreach (string directory in Directory.GetDirectories(args[0], "_m*"))
{
    string id = Path.GetFileName(directory)[1..];
    string path = Path.Combine(directory, id + ".bin");
    if (!File.Exists(path)) continue;
    try
    {
        byte[] monster = File.ReadAllBytes(path);
        int aiOffset = BitConverter.ToInt32(monster, 0x04);
        int workerOffset = BitConverter.ToInt32(monster, 0x08);
        if (aiOffset <= 0 || workerOffset <= aiOffset) throw new InvalidDataException("Invalid monster AI chunk pointers.");
        byte[] ai = monster.AsSpan(aiOffset, workerOffset - aiOffset).ToArray();
        AtelScriptDocument document = AtelScriptDocument.Read(ai);
		byte[] normalizedAi = document.Bytes.ToArray();
		if (document.RecoveredMissingCodeLength) recoveredHeaders++;
        byte[] hexRoundTrip = AtelScriptDocument.ParseHexEditorText(document.ToHexEditorText());
        document.ReplaceBytes(hexRoundTrip);
        if (!normalizedAi.SequenceEqual(document.Bytes)) throw new InvalidDataException("Hex round trip changed normalized bytes.");
        parsed++;
    }
    catch (Exception ex)
    {
        failures.Add($"{path}: {ex.Message}");
    }
}

Console.WriteLine($"Parsed={parsed} RecoveredHeaders={recoveredHeaders} Failed={failures.Count}");
foreach (string failure in failures) Console.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;
