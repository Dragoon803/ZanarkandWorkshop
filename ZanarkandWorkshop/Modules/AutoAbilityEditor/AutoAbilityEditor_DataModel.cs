using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.IO;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.AutoAbilityEditor;

internal partial class AutoAbilityEditor_DataModel : ObservableObject
{
    private byte[] _abilityFile = Array.Empty<byte>();
    private byte[] _recipeFile = Array.Empty<byte>();
    private int _abilityStart;
    private int _abilityCount;
    private byte[] _baselineAbility = Array.Empty<byte>();
    private byte[] _baselineRecipe = Array.Empty<byte>();
    private byte[] _historyAbility = Array.Empty<byte>();
    private byte[] _historyRecipe = Array.Empty<byte>();
    private readonly Stack<HistorySnapshot> _undoHistory = new();
    private readonly Stack<HistorySnapshot> _redoHistory = new();
    private bool _restoringHistory;

    private sealed record HistorySnapshot(byte[] Ability, byte[] Recipe, ushort? SelectedId);

    public List<AutoAbilityEntry> AllAbilities { get; } = new();
    public ObservableCollection<AutoAbilityEntry> DisplayedAbilities { get; } = new();
    public IReadOnlyList<RecipeItemOption> ItemOptions { get; } =
        Item_Dictionary.Instance.OrderBy(pair => pair.Key)
            .Select(pair => new RecipeItemOption((ushort)(0x2000 + pair.Key), pair.Value)).ToList();

    [ObservableProperty] private string filterText = "";
    [ObservableProperty] private AutoAbilityEntry? selectedAbility;
    public bool HasSelectedAbility => SelectedAbility is not null;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string historyStatus = "";
    [ObservableProperty] private bool hasAbilityFile;
    [ObservableProperty] private bool hasRecipeFile;
    [ObservableProperty] private bool isDirty;
    public bool CanUndo => _undoHistory.Count > 0;
    public bool CanRedo => _redoHistory.Count > 0;
    public bool CanUndoAll => IsDirty;
    public string PropertiesTabHeader => HasAbilityFile
        ? "Properties & Effects"
        : "Properties & Effects (a_ability.bin missing)";
    public string RecipeTabHeader => HasRecipeFile
        ? "Recipe"
        : "Recipe (kaizou.bin missing)";
    public int InitialTabIndex => HasAbilityFile ? 0 : 1;

    public AutoAbilityEditor_DataModel()
    {
        Load();
    }

    partial void OnSelectedAbilityChanged(AutoAbilityEntry? value) =>
        OnPropertyChanged(nameof(HasSelectedAbility));

    public void Load()
    {
        string abilityPath = Project_Service.Instance.Path_KernelAutoAbilityUs;
        string recipePath = Project_Service.Instance.Path_KernelCustomization;
        HasAbilityFile = File.Exists(abilityPath);
        HasRecipeFile = File.Exists(recipePath);
        OnPropertyChanged(nameof(PropertiesTabHeader));
        OnPropertyChanged(nameof(RecipeTabHeader));
        OnPropertyChanged(nameof(InitialTabIndex));
        if (!HasAbilityFile && !HasRecipeFile)
            throw new FileNotFoundException("Neither a_ability.bin nor kaizou.bin is available.");

        byte[] abilityBytes = HasAbilityFile ? File.ReadAllBytes(abilityPath) : Array.Empty<byte>();
        byte[] recipeBytes = HasRecipeFile ? File.ReadAllBytes(recipePath) : Array.Empty<byte>();
        LoadFromBytes(abilityBytes, recipeBytes, true, null);
    }

    private void LoadFromBytes(byte[] abilityBytes, byte[] recipeBytes, bool resetBaseline,
        ushort? selectedAbilityId)
    {
        var recipes = new Dictionary<ushort, RecipeRecord>();
        if (HasRecipeFile)
        {
            _recipeFile = recipeBytes.ToArray();
            if (_recipeFile.Length < 0x14 + 125 * 8)
                throw new InvalidDataException("kaizou.bin does not contain 125 complete recipe records.");
            for (int i = 0; i < 125; i++)
            {
                int offset = 0x14 + i * 8;
                ushort abilityId = BitConverter.ToUInt16(_recipeFile, offset + 2);
                var recipe = new RecipeRecord(_recipeFile, offset, ItemOptions);
                recipes[abilityId] = recipe;
            }
        }
        else
            _recipeFile = Array.Empty<byte>();

        AllAbilities.Clear();
        int matchedRecipeCount = 0;
        if (HasAbilityFile)
        {
            _abilityFile = abilityBytes.ToArray();
            if (_abilityFile.Length < 0x14) throw new InvalidDataException("a_ability.bin is too short.");
            ushort minimumId = BitConverter.ToUInt16(_abilityFile, 0x08);
            ushort maximumId = BitConverter.ToUInt16(_abilityFile, 0x0A);
            ushort recordSize = BitConverter.ToUInt16(_abilityFile, 0x0C);
            _abilityStart = BitConverter.ToInt32(_abilityFile, 0x10);
            int count = maximumId - minimumId + 1;
            _abilityCount = count;
            if (recordSize != 0x6C) throw new InvalidDataException($"Unexpected auto-ability record size 0x{recordSize:X}.");
            if (_abilityStart < 0x14 || _abilityStart + count * recordSize > _abilityFile.Length)
                throw new InvalidDataException("The auto-ability record table is outside a_ability.bin.");

            for (int i = 0; i < count; i++)
            {
                ushort id = (ushort)(0x8000 + minimumId + i);
                ushort dictionaryIndex = (ushort)(minimumId + i);
                string name = AutoAbility_Dictionary.Instance.TryGetValue(dictionaryIndex, out string? known)
                    ? known : $"Auto Ability 0x{id:X4}";
                recipes.TryGetValue(id, out RecipeRecord? recipe);
                if (recipe != null) matchedRecipeCount++;
                int recordOffset = _abilityStart + i * recordSize;
                byte[][] textScripts = ReadTextScripts(_abilityFile, recordOffset,
                    _abilityStart + count * recordSize);
                string decodedName = DecodeText(textScripts[0], name);
                string decodedDescription = DecodeText(textScripts[4], "");
                AllAbilities.Add(new AutoAbilityEntry(id, decodedName, decodedDescription,
                    _abilityFile, recordOffset, recipe, textScripts));
            }
        }
        else
        {
            _abilityFile = Array.Empty<byte>();
            _abilityStart = 0;
            _abilityCount = 0;
            foreach ((ushort id, RecipeRecord recipe) in recipes.OrderBy(pair => pair.Key))
            {
                ushort dictionaryIndex = (ushort)(id & 0x0FFF);
                string name = AutoAbility_Dictionary.Instance.TryGetValue(dictionaryIndex, out string? known)
                    ? known : $"Auto Ability 0x{id:X4}";
                AllAbilities.Add(new AutoAbilityEntry(
                    id, name, "", new byte[0x6C], 0, recipe,
                    Enumerable.Range(0, 8).Select(_ => Array.Empty<byte>()).ToArray()));
            }
        }
        if (HasAbilityFile && HasRecipeFile && recipes.Count > 0 && matchedRecipeCount == 0)
            throw new InvalidDataException(
                "No kaizou.bin recipes matched the auto-ability IDs in a_ability.bin.");
        ApplyFilter();
        if (selectedAbilityId.HasValue)
            SelectedAbility = DisplayedAbilities.FirstOrDefault(entry => entry.Id == selectedAbilityId.Value);
        Status = HasAbilityFile && HasRecipeFile
            ? $"Loaded {AllAbilities.Count} auto abilities; matched {matchedRecipeCount} customization recipes."
            : HasAbilityFile
                ? $"Loaded {AllAbilities.Count} auto abilities. Recipe editing is unavailable because kaizou.bin is missing."
                : $"Loaded {AllAbilities.Count} customization recipes. Property editing is unavailable because a_ability.bin is missing.";
        byte[] currentAbility = HasAbilityFile ? BuildAbilityFile() : Array.Empty<byte>();
        if (resetBaseline)
        {
            _baselineAbility = currentAbility.ToArray();
            _baselineRecipe = _recipeFile.ToArray();
            _undoHistory.Clear();
            _redoHistory.Clear();
            HistoryStatus = "";
        }
        _historyAbility = currentAbility.ToArray();
        _historyRecipe = _recipeFile.ToArray();
        IsDirty = !currentAbility.SequenceEqual(_baselineAbility) ||
                  !_recipeFile.SequenceEqual(_baselineRecipe);
        NotifyHistoryState();
    }

    public void ApplyFilter()
    {
        AutoAbilityEntry? selection = SelectedAbility;
        DisplayedAbilities.Clear();
        string filter = FilterText.Trim();
        foreach (AutoAbilityEntry entry in AllAbilities.Where(entry =>
                     filter.Length == 0 ||
                     entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     entry.DisplayId.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            DisplayedAbilities.Add(entry);
        if (selection != null && DisplayedAbilities.Contains(selection)) SelectedAbility = selection;
    }

    public void Save()
    {
        ushort? selectedAbilityId = SelectedAbility?.Id;
        Validate();
        var replacements = new List<FileReplacement>();
        if (HasAbilityFile)
        {
            byte[] rebuiltAbilityFile = BuildAbilityFile();
            VerifyRebuiltAbilityFile(rebuiltAbilityFile);
            replacements.Add(new FileReplacement(
                Project_Service.Instance.Path_KernelAutoAbilityUs, rebuiltAbilityFile));
        }
        if (HasRecipeFile)
            replacements.Add(new FileReplacement(
                Project_Service.Instance.Path_KernelCustomization, _recipeFile));
        MultiFileSaveTransaction.Save(replacements);
        Load();
        if (selectedAbilityId.HasValue)
        {
            AutoAbilityEntry? restoredSelection =
                DisplayedAbilities.FirstOrDefault(entry => entry.Id == selectedAbilityId.Value);
            if (restoredSelection != null) SelectedAbility = restoredSelection;
        }
        Status = EditorSaveStatus.Success("Auto Ability");
    }

    public void SaveToMaster(string masterPath)
    {
        Validate();
        if (HasAbilityFile)
        {
            byte[] rebuilt = BuildAbilityFile();
            VerifyRebuiltAbilityFile(rebuilt);
            File.WriteAllBytes(Path.Combine(masterPath, "new_uspc", "battle", "kernel", "a_ability.bin"), rebuilt);
        }
        if (HasRecipeFile)
            File.WriteAllBytes(Path.Combine(masterPath, "jppc", "battle", "kernel", "kaizou.bin"), _recipeFile);
    }

    public void RefreshDirtyState()
    {
        byte[] ability = HasAbilityFile ? BuildAbilityFile() : Array.Empty<byte>();
        if (!_restoringHistory &&
            (!ability.SequenceEqual(_historyAbility) || !_recipeFile.SequenceEqual(_historyRecipe)))
        {
            _undoHistory.Push(new HistorySnapshot(
                _historyAbility.ToArray(), _historyRecipe.ToArray(), SelectedAbility?.Id));
            _redoHistory.Clear();
            _historyAbility = ability.ToArray();
            _historyRecipe = _recipeFile.ToArray();
        }
        IsDirty = !ability.SequenceEqual(_baselineAbility) ||
                  !_recipeFile.SequenceEqual(_baselineRecipe);
        NotifyHistoryState();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        HistorySnapshot current = CurrentSnapshot();
        HistorySnapshot target = _undoHistory.Pop();
        _redoHistory.Push(current);
        string detail = DescribeHistoryChange(target, current);
        RestoreHistory(target);
        HistoryStatus = $"Undid: {detail}.";
    }

    public void Redo()
    {
        if (!CanRedo) return;
        HistorySnapshot current = CurrentSnapshot();
        HistorySnapshot target = _redoHistory.Pop();
        _undoHistory.Push(current);
        string detail = DescribeHistoryChange(current, target);
        RestoreHistory(target);
        HistoryStatus = $"Redid: {detail}.";
    }

    public void UndoAll()
    {
        if (!IsDirty) return;
        int appliedCount = _undoHistory.Count;

        // Preserve the individual edit timeline. The old implementation put
        // only the complete current snapshot in Redo, which made the first
        // Redo after Undo All restore every change at once.
        List<HistorySnapshot> timeline = _undoHistory.Reverse().ToList();
        timeline.Add(CurrentSnapshot());
        timeline.AddRange(_redoHistory.ToArray());

        // The first timeline entry is the saved baseline. Stack the remaining
        // snapshots in reverse so the earliest individual change is the next
        // Redo target.
        _undoHistory.Clear();
        _redoHistory.Clear();
        for (int i = timeline.Count - 1; i >= 1; i--)
            _redoHistory.Push(timeline[i]);

        RestoreHistory(new HistorySnapshot(
            _baselineAbility.ToArray(), _baselineRecipe.ToArray(), SelectedAbility?.Id));
        HistoryStatus = $"Undid all: {appliedCount} Auto Ability change{(appliedCount == 1 ? "" : "s")} since the last save.";
    }

    private static string DescribeHistoryChange(HistorySnapshot before, HistorySnapshot after)
    {
        int ability = CountChangedBytes(before.Ability, after.Ability);
        int recipe = CountChangedBytes(before.Recipe, after.Recipe);
        string item = after.SelectedId is ushort id ? $"ability #{id}" : "Auto Ability data";
        return $"{item} ({ability} ability byte{(ability == 1 ? "" : "s")}, {recipe} recipe byte{(recipe == 1 ? "" : "s")} changed)";
    }

    private static int CountChangedBytes(byte[] left, byte[] right)
    {
        int count = Math.Abs(left.Length - right.Length);
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++) if (left[i] != right[i]) count++;
        return count;
    }

    private HistorySnapshot CurrentSnapshot() => new(
        (HasAbilityFile ? BuildAbilityFile() : Array.Empty<byte>()).ToArray(),
        _recipeFile.ToArray(), SelectedAbility?.Id);

    private void RestoreHistory(HistorySnapshot snapshot)
    {
        _restoringHistory = true;
        try { LoadFromBytes(snapshot.Ability, snapshot.Recipe, false, snapshot.SelectedId); }
        finally { _restoringHistory = false; }
        NotifyHistoryState();
    }

    private void NotifyHistoryState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
    }

    public void RestoreOriginalAndSave(string originalPath, bool restoreAbilityFile)
    {
        ushort? selectedAbilityId = SelectedAbility?.Id;
        string projectPath = restoreAbilityFile
            ? Project_Service.Instance.Path_KernelAutoAbilityUs
            : Project_Service.Instance.Path_KernelCustomization;
        string expectedName = restoreAbilityFile ? "a_ability.bin" : "kaizou.bin";
        if (!File.Exists(originalPath))
            throw new FileNotFoundException($"The original {expectedName} file could not be found.", originalPath);

        File.Copy(originalPath, projectPath, true);
        Load();
        if (selectedAbilityId.HasValue)
        {
            AutoAbilityEntry? restoredSelection =
                DisplayedAbilities.FirstOrDefault(entry => entry.Id == selectedAbilityId.Value);
            if (restoredSelection != null) SelectedAbility = restoredSelection;
        }
        Status = $"Restored and reloaded the original {expectedName}.";
    }

    public void RestoreSelectedOriginalAbilityAndSave(string originalAbilityPath, string? originalRecipePath)
    {
        AutoAbilityEntry selected = SelectedAbility ??
            throw new InvalidOperationException("Select an Auto Ability to restore.");
        if (!HasAbilityFile)
            throw new InvalidOperationException("a_ability.bin is required to restore an Auto Ability.");
        if (!File.Exists(originalAbilityPath))
            throw new FileNotFoundException("The original a_ability.bin could not be found.", originalAbilityPath);

        byte[] originalFile = File.ReadAllBytes(originalAbilityPath);
        (int start, int count, ushort minimumId) = ReadAbilityTable(originalFile, "original a_ability.bin");
        int rawId = selected.Id - 0x8000;
        int originalIndex = rawId - minimumId;
        if (originalIndex < 0 || originalIndex >= count)
            throw new InvalidOperationException(
                $"{selected.DisplayId} does not exist in the original a_ability.bin.");

        int originalRecordOffset = start + originalIndex * 0x6C;
        int originalTextStart = start + count * 0x6C;
        byte[][] originalScripts = ReadTextScripts(originalFile, originalRecordOffset, originalTextStart);
        byte[] originalRecord = originalFile.Skip(originalRecordOffset).Take(0x6C).ToArray();
        selected.ApplyOriginalRecord(originalRecord, originalScripts,
            DecodeText(originalScripts[0], selected.Name), DecodeText(originalScripts[4], ""));

        bool restoredRecipe = false;
        if (selected.Recipe != null && HasRecipeFile &&
            !string.IsNullOrWhiteSpace(originalRecipePath) && File.Exists(originalRecipePath))
        {
            byte[] originalRecipes = File.ReadAllBytes(originalRecipePath);
            if (originalRecipes.Length < 0x14 + 125 * 8)
                throw new InvalidDataException("The original kaizou.bin does not contain 125 complete recipes.");
            for (int index = 0; index < 125; index++)
            {
                int offset = 0x14 + index * 8;
                if (BitConverter.ToUInt16(originalRecipes, offset + 2) != selected.Id) continue;
                selected.Recipe.ApplyOriginalRecord(originalRecipes.AsSpan(offset, 8));
                restoredRecipe = true;
                break;
            }
        }

        ApplyFilter();
        SelectedAbility = selected;
        RefreshDirtyState();
        string restoredName = selected.Name;
        Save();
        Status = restoredRecipe
            ? $"Restored and saved {restoredName} properties, text, and recipe."
            : $"Restored and saved {restoredName} properties and text.";
    }

    private static (int Start, int Count, ushort MinimumId) ReadAbilityTable(byte[] file, string label)
    {
        if (file.Length < 0x14) throw new InvalidDataException($"The {label} is too short.");
        ushort minimumId = BitConverter.ToUInt16(file, 0x08);
        ushort maximumId = BitConverter.ToUInt16(file, 0x0A);
        ushort recordSize = BitConverter.ToUInt16(file, 0x0C);
        int start = BitConverter.ToInt32(file, 0x10);
        if (maximumId < minimumId) throw new InvalidDataException($"The {label} has an invalid ID range.");
        int count = maximumId - minimumId + 1;
        if (recordSize != 0x6C)
            throw new InvalidDataException($"The {label} uses record size 0x{recordSize:X}, not 0x6C.");
        if (start < 0x14 || start + count * recordSize > file.Length)
            throw new InvalidDataException($"The Auto Ability table is outside the {label}.");
        return (start, count, minimumId);
    }

    private void Validate()
    {
        if (HasAbilityFile && BitConverter.ToUInt16(_abilityFile, 0x0C) != 0x6C)
            throw new InvalidDataException("The auto-ability record size changed unexpectedly.");
        if (HasRecipeFile)
            foreach (AutoAbilityEntry entry in AllAbilities)
                entry.Recipe?.Validate();
    }

    private byte[] BuildAbilityFile()
    {
        int textStart = _abilityStart + _abilityCount * 0x6C;
        byte[] recordsAndHeader = _abilityFile[..textStart];
        using var textPool = new MemoryStream();
        var offsetsByContent = new Dictionary<string, ushort>();

        for (int entryIndex = 0; entryIndex < AllAbilities.Count; entryIndex++)
        {
            AutoAbilityEntry entry = AllAbilities[entryIndex];
            byte[][] scripts = entry.GetScriptsForSave();
            int recordOffset = _abilityStart + entryIndex * 0x6C;
            for (int pointerIndex = 0; pointerIndex < scripts.Length; pointerIndex++)
            {
                byte[] script = scripts[pointerIndex];
                string key = Convert.ToHexString(script);
                if (!offsetsByContent.TryGetValue(key, out ushort offset))
                {
                    if (textPool.Length + script.Length + 1 > ushort.MaxValue + 1L)
                        throw new InvalidDataException("The rebuilt auto-ability text section exceeds the 16-bit offset limit.");
                    offset = checked((ushort)textPool.Length);
                    offsetsByContent[key] = offset;
                    textPool.Write(script);
                    textPool.WriteByte(0);
                }
                recordsAndHeader[recordOffset + pointerIndex * 2] = (byte)offset;
                recordsAndHeader[recordOffset + pointerIndex * 2 + 1] = (byte)(offset >> 8);
            }
        }
        return recordsAndHeader.Concat(textPool.ToArray()).ToArray();
    }

    private void VerifyRebuiltAbilityFile(byte[] rebuilt)
    {
        int textStart = _abilityStart + _abilityCount * 0x6C;
        if (rebuilt.Length <= textStart) throw new InvalidDataException("The rebuilt text section is empty.");
        for (int entryIndex = 0; entryIndex < AllAbilities.Count; entryIndex++)
        {
            int recordOffset = _abilityStart + entryIndex * 0x6C;
            byte[][] scripts = ReadTextScripts(rebuilt, recordOffset, textStart);
            string name = DecodeText(scripts[0], "");
            string description = DecodeText(scripts[4], "");
            if (!string.Equals(name, AllAbilities[entryIndex].Name, StringComparison.Ordinal) ||
                !string.Equals(description, AllAbilities[entryIndex].Description, StringComparison.Ordinal))
                throw new InvalidDataException($"Text verification failed for auto ability {AllAbilities[entryIndex].DisplayId}.");
        }
    }

    private static byte[][] ReadTextScripts(byte[] file, int recordOffset, int textStart)
    {
        byte[] textPool = file[textStart..];
        var scripts = new byte[8][];
        for (int pointerIndex = 0; pointerIndex < scripts.Length; pointerIndex++)
        {
            ushort offset = BitConverter.ToUInt16(file, recordOffset + pointerIndex * 2);
            if (offset >= textPool.Length)
                throw new InvalidDataException($"Text offset 0x{offset:X4} is outside the auto-ability text section.");
            scripts[pointerIndex] = FfxEncoding.GetScriptBytesFromTextFile(textPool, offset);
        }
        return scripts;
    }

    private static string DecodeText(byte[] script, string fallback)
    {
        try { return FfxEncoding.DecodeEditableTextScript(script, FfxEncoding.UsDecoder); }
        catch { return fallback; }
    }
}

internal sealed class AutoAbilityEntry : ObservableObject
{
    private readonly byte[] _file;
    private readonly int _offset;
    public ushort Id { get; }
    private string _name;
    private string _description;
    private bool _nameDirty;
    private bool _descriptionDirty;
    private readonly byte[][] _textScripts;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                _nameDirty = true;
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }
    public string Description
    {
        get => _description;
        set { if (SetProperty(ref _description, value)) _descriptionDirty = true; }
    }
    public string DisplayName => Name;
    public string DisplayId => $"0x{Id:X4}";
    public RecipeRecord? Recipe { get; }
    public bool HasRecipe => Recipe is not null;
    public bool HasNoRecipe => Recipe is null;

    public AutoAbilityEntry(ushort id, string name, string description, byte[] file, int offset,
        RecipeRecord? recipe, byte[][] textScripts)
    {
        Id = id; _name = name; _description = description; _file = file; _offset = offset;
        Recipe = recipe; _textScripts = textScripts;
    }

    public byte[][] GetScriptsForSave()
    {
        byte[][] scripts = _textScripts.Select(script => script.ToArray()).ToArray();
        if (_nameDirty) scripts[0] = FfxEncoding.EncodeTextScript(Name, FfxEncoding.UsEncoder);
        if (_descriptionDirty) scripts[4] = FfxEncoding.EncodeTextScript(Description, FfxEncoding.UsEncoder);
        return scripts;
    }

    internal void ApplyOriginalRecord(
        byte[] originalRecord, byte[][] originalScripts, string originalName, string originalDescription)
    {
        if (originalRecord.Length != 0x6C || originalScripts.Length != 8)
            throw new InvalidDataException("The original Auto Ability record is incomplete.");

        // Text pointers belong to the source file. Restore only the structured data here;
        // BuildAbilityFile recalculates every pointer for the adjusted destination file.
        Array.Copy(originalRecord, 0x10, _file, _offset + 0x10, 0x5C);
        for (int index = 0; index < _textScripts.Length; index++)
            _textScripts[index] = originalScripts[index].ToArray();
        _name = originalName;
        _description = originalDescription;
        _nameDirty = false;
        _descriptionDirty = false;
        _statusEffects = null;
        _extraStatusEffects = null;
        _effects = null;
        OnPropertyChanged(string.Empty);
    }

    private bool Bit(int relativeOffset, int bit) => (_file[_offset + relativeOffset] & (1 << bit)) != 0;
    private void Bit(int relativeOffset, int bit, bool value)
    {
        byte mask = (byte)(1 << bit);
        _file[_offset + relativeOffset] = value
            ? (byte)(_file[_offset + relativeOffset] | mask)
            : (byte)(_file[_offset + relativeOffset] & ~mask);
    }

    public bool IsSos { get => _file[_offset + 0x10] != 0; set => _file[_offset + 0x10] = value ? (byte)1 : (byte)0; }
    public byte StatIncreaseAmount { get => _file[_offset + 0x55]; set => _file[_offset + 0x55] = value; }
    public byte Icon { get => _file[_offset + 0x68]; set => _file[_offset + 0x68] = value; }
    public byte GroupIndex { get => _file[_offset + 0x69]; set => _file[_offset + 0x69] = value; }
    public byte GroupLevel { get => _file[_offset + 0x6A]; set => _file[_offset + 0x6A] = value; }
    public byte InternationalBonusIndex { get => _file[_offset + 0x6B]; set => _file[_offset + 0x6B] = value; }

    public bool FireStrike { get => Bit(0x11, 0); set => Bit(0x11, 0, value); }
    public bool IceStrike { get => Bit(0x11, 1); set => Bit(0x11, 1, value); }
    public bool ThunderStrike { get => Bit(0x11, 2); set => Bit(0x11, 2, value); }
    public bool WaterStrike { get => Bit(0x11, 3); set => Bit(0x11, 3, value); }
    public bool HolyStrike { get => Bit(0x11, 4); set => Bit(0x11, 4, value); }
    public bool FireAbsorb { get => Bit(0x12, 0); set => Bit(0x12, 0, value); }
    public bool IceAbsorb { get => Bit(0x12, 1); set => Bit(0x12, 1, value); }
    public bool ThunderAbsorb { get => Bit(0x12, 2); set => Bit(0x12, 2, value); }
    public bool WaterAbsorb { get => Bit(0x12, 3); set => Bit(0x12, 3, value); }
    public bool HolyAbsorb { get => Bit(0x12, 4); set => Bit(0x12, 4, value); }
    public bool FireIgnore { get => Bit(0x13, 0); set => Bit(0x13, 0, value); }
    public bool IceIgnore { get => Bit(0x13, 1); set => Bit(0x13, 1, value); }
    public bool ThunderIgnore { get => Bit(0x13, 2); set => Bit(0x13, 2, value); }
    public bool WaterIgnore { get => Bit(0x13, 3); set => Bit(0x13, 3, value); }
    public bool HolyIgnore { get => Bit(0x13, 4); set => Bit(0x13, 4, value); }
    public bool FireResist { get => Bit(0x14, 0); set => Bit(0x14, 0, value); }
    public bool IceResist { get => Bit(0x14, 1); set => Bit(0x14, 1, value); }
    public bool ThunderResist { get => Bit(0x14, 2); set => Bit(0x14, 2, value); }
    public bool WaterResist { get => Bit(0x14, 3); set => Bit(0x14, 3, value); }
    public bool HolyResist { get => Bit(0x14, 4); set => Bit(0x14, 4, value); }
    public bool FireWeak { get => Bit(0x15, 0); set => Bit(0x15, 0, value); }
    public bool IceWeak { get => Bit(0x15, 1); set => Bit(0x15, 1, value); }
    public bool ThunderWeak { get => Bit(0x15, 2); set => Bit(0x15, 2, value); }
    public bool WaterWeak { get => Bit(0x15, 3); set => Bit(0x15, 3, value); }
    public bool HolyWeak { get => Bit(0x15, 4); set => Bit(0x15, 4, value); }

    public bool Strength { get => Bit(0x56, 0); set => Bit(0x56, 0, value); }
    public bool Defense { get => Bit(0x56, 1); set => Bit(0x56, 1, value); }
    public bool Magic { get => Bit(0x56, 2); set => Bit(0x56, 2, value); }
    public bool MagicDefense { get => Bit(0x56, 3); set => Bit(0x56, 3, value); }
    public bool Agility { get => Bit(0x56, 4); set => Bit(0x56, 4, value); }
    public bool Luck { get => Bit(0x56, 5); set => Bit(0x56, 5, value); }
    public bool Evasion { get => Bit(0x56, 6); set => Bit(0x56, 6, value); }
    public bool Accuracy { get => Bit(0x56, 7); set => Bit(0x56, 7, value); }
    public bool Hp { get => Bit(0x57, 0); set => Bit(0x57, 0, value); }
    public bool Mp { get => Bit(0x57, 1); set => Bit(0x57, 1, value); }
    public bool StrengthBonus { get => Bit(0x57, 2); set => Bit(0x57, 2, value); }
    public bool MagicBonus { get => Bit(0x57, 3); set => Bit(0x57, 3, value); }
    public bool DefenseBonus { get => Bit(0x57, 4); set => Bit(0x57, 4, value); }
    public bool MagicDefenseBonus { get => Bit(0x57, 5); set => Bit(0x57, 5, value); }

    public IReadOnlyList<StatusEffectRow> StatusEffects => _statusEffects ??= StandardStatusNames
        .Select((name, index) => new StatusEffectRow(name, index, _file, _offset)).ToList();
    public IReadOnlyList<StatusEffectRow> PermanentStatuses => StatusEffects.Take(12).ToList();
    public IReadOnlyList<StatusEffectRow> TemporaryStatuses => StatusEffects.Skip(12).ToList();
    private List<StatusEffectRow>? _statusEffects;
    private static readonly string[] StandardStatusNames =
    {
        "Death", "Zombie", "Petrify", "Poison", "Power Break", "Magic Break",
        "Armor Break", "Mental Break", "Confuse", "Berserk", "Provoke", "Threaten",
        "Sleep", "Silence", "Darkness", "Shell", "Protect", "Reflect", "NulWater",
        "NulFire", "NulThunder", "NulBlizzard", "Regen", "Haste", "Slow"
    };

    public IReadOnlyList<ExtraStatusEffectRow> ExtraStatusEffects => _extraStatusEffects ??=
        ExtraStatusNames.Select((name, index) =>
            new ExtraStatusEffectRow(name, index, _file, _offset)).ToList();
    private List<ExtraStatusEffectRow>? _extraStatusEffects;
    private static readonly string[] ExtraStatusNames =
    {
        "Scan", "Distill Power", "Distill Mana", "Distill Speed", "Distill Move",
        "Distill Ability", "Shield", "Boost", "Eject", "Auto-Life", "Curse",
        "Defend", "Guard", "Sentinel", "Doom"
    };

    public IReadOnlyList<EffectFlag> EffectFlags => _effects ??= EffectDefinitions
        .Select(def => new EffectFlag(def.Name, () => Bit(def.Offset, def.Bit), value => Bit(def.Offset, def.Bit, value))).ToList();
    private List<EffectFlag>? _effects;
    private static readonly (string Name, int Offset, int Bit)[] EffectDefinitions =
    {
        ("Sensor",0x62,0),("First Strike",0x62,1),("Initiative",0x62,2),("Counterattack",0x62,3),
        ("Evade & Counter",0x62,4),("Magic Counter",0x62,5),("Magic Booster",0x62,6),("Alchemy",0x63,1),
        ("Auto-Potion",0x63,2),("Auto-Med",0x63,3),("Auto-Phoenix",0x63,4),("Piercing",0x63,5),
        ("Half MP Cost",0x63,6),("One MP Cost",0x63,7),("Double Overdrive",0x64,0),
        ("Triple Overdrive",0x64,1),("SOS Overdrive",0x64,2),("Overdrive to AP",0x64,3),
        ("Double AP",0x64,4),("Triple AP",0x64,5),("No AP",0x64,6),("Pickpocket",0x64,7),
        ("Master Thief",0x65,0),("Break HP Limit",0x65,1),("Break MP Limit",0x65,2),
        ("Break Damage Limit",0x65,3),("Gillionaire",0x65,6),("HP Stroll",0x65,7),
        ("MP Stroll",0x66,0),("No Encounters",0x66,1),("Capture",0x66,2)
    };

}

internal sealed class EffectFlag
{
    private readonly Func<bool> _get; private readonly Action<bool> _set;
    public string Name { get; }
    public bool Value { get => _get(); set => _set(value); }
    public EffectFlag(string name, Func<bool> get, Action<bool> set) { Name = name; _get = get; _set = set; }
}

internal sealed class StatusEffectRow
{
    private readonly byte[] _file;
    private readonly int _recordOffset;
    private readonly int _index;
    public string Name { get; }
    public bool HasDuration => _index >= 12;

    public StatusEffectRow(string name, int index, byte[] file, int recordOffset)
    {
        Name = name; _index = index; _file = file; _recordOffset = recordOffset;
    }

    public byte InflictChance
    {
        get => _file[_recordOffset + 0x16 + _index];
        set => _file[_recordOffset + 0x16 + _index] = value;
    }
    public byte Duration
    {
        get => HasDuration ? _file[_recordOffset + 0x2F + _index - 12] : (byte)0;
        set { if (HasDuration) _file[_recordOffset + 0x2F + _index - 12] = value; }
    }
    public byte ResistChance
    {
        get => _file[_recordOffset + 0x3C + _index];
        set => _file[_recordOffset + 0x3C + _index] = value;
    }
    public bool Auto
    {
        get
        {
            int relativeOffset = _index < 12 ? 0x58 + _index / 8 : 0x5A + (_index - 12) / 8;
            int bit = _index < 12 ? _index % 8 : (_index - 12) % 8;
            return (_file[_recordOffset + relativeOffset] & (1 << bit)) != 0;
        }
        set
        {
            int relativeOffset = _index < 12 ? 0x58 + _index / 8 : 0x5A + (_index - 12) / 8;
            int bit = _index < 12 ? _index % 8 : (_index - 12) % 8;
            byte mask = (byte)(1 << bit);
            _file[_recordOffset + relativeOffset] = value
                ? (byte)(_file[_recordOffset + relativeOffset] | mask)
                : (byte)(_file[_recordOffset + relativeOffset] & ~mask);
        }
    }
}

internal sealed class ExtraStatusEffectRow
{
    private readonly byte[] _file;
    private readonly int _recordOffset;
    private readonly int _index;
    public string Name { get; }
    public ExtraStatusEffectRow(string name, int index, byte[] file, int recordOffset)
    {
        Name = name; _index = index; _file = file; _recordOffset = recordOffset;
    }
    public bool Auto { get => Get(0x5C); set => Set(0x5C, value); }
    public bool Inflict { get => Get(0x5E); set => Set(0x5E, value); }
    public bool Resist { get => Get(0x60); set => Set(0x60, value); }
    private bool Get(int relativeOffset) =>
        (_file[_recordOffset + relativeOffset + _index / 8] & (1 << (_index % 8))) != 0;
    private void Set(int relativeOffset, bool value)
    {
        int offset = _recordOffset + relativeOffset + _index / 8;
        byte mask = (byte)(1 << (_index % 8));
        _file[offset] = value ? (byte)(_file[offset] | mask) : (byte)(_file[offset] & ~mask);
    }
}

internal sealed class RecipeRecord : ObservableObject
{
    private readonly byte[] _file; private readonly int _offset;
    public IReadOnlyList<RecipeItemOption> ItemOptions { get; }
    public RecipeRecord(byte[] file, int offset, IReadOnlyList<RecipeItemOption> items)
    {
        _file = file; _offset = offset; ItemOptions = items;
    }
    public ushort AbilityId => BitConverter.ToUInt16(_file, _offset + 2);
    public bool IsWeapon { get => _file[_offset] == 1; set { if (value) _file[_offset] = 1; } }
    public bool IsArmor { get => _file[_offset] == 2; set { if (value) _file[_offset] = 2; } }
    public RecipeItemOption? SelectedItem
    {
        get { ushort id = BitConverter.ToUInt16(_file, _offset + 4); return ItemOptions.FirstOrDefault(x => x.Id == id); }
        set { if (value != null) WriteUInt16(_offset + 4, value.Id); }
    }
    public byte Quantity { get => _file[_offset + 6]; set => _file[_offset + 6] = value; }
    public void Validate()
    {
        if (_file[_offset] is not (1 or 2)) throw new InvalidDataException("A recipe has an invalid equipment type.");
        if (SelectedItem == null) throw new InvalidDataException("A recipe refers to an unknown item.");
        if (Quantity > 99) throw new InvalidDataException("A recipe quantity exceeds the in-game item limit of 99.");
    }
    private void WriteUInt16(int offset, ushort value)
    { _file[offset] = (byte)value; _file[offset + 1] = (byte)(value >> 8); }
    internal void ApplyOriginalRecord(ReadOnlySpan<byte> record)
    {
        if (record.Length != 8) throw new InvalidDataException("The original recipe record is incomplete.");
        record.CopyTo(_file.AsSpan(_offset, 8));
        OnPropertyChanged(string.Empty);
    }
}

internal sealed record RecipeItemOption(ushort Id, string Name)
{
    public string Display => $"{Name} (0x{Id:X4})";
}
