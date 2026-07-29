using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MixEditor;

internal partial class MixEditor_DataModel : ObservableObject
{
    internal const int HeaderSize = 0x14;
    internal const int IngredientCount = 112;
    internal const int RecordSize = IngredientCount * sizeof(ushort);
    internal const ushort FirstResultId = 0x308B;
    internal const ushort LastResultId = 0x30CA;
    internal const ushort CommandCategory = 0x3000;

    private byte[] _file = Array.Empty<byte>();
    public List<MixRecipeEntry> AllRecipes { get; } = new();
    public ObservableCollection<MixRecipeEntry> DisplayedRecipes { get; } = new();
    public IReadOnlyList<MixResultOption> ResultOptions { get; private set; } =
        Array.Empty<MixResultOption>();
    public IReadOnlyList<MixIngredientOption> IngredientOptions { get; } =
        Enumerable.Range(0, IngredientCount)
            .Select(index => new MixIngredientOption(index, ItemName(index))).ToList();

    [ObservableProperty] private string filterText = "";
    [ObservableProperty] private string status = "";

    public MixEditor_DataModel() => Load();

    public void Load()
    {
        string path = Project_Service.Instance.Path_KernelMixRecipes;
        _file = File.ReadAllBytes(path);
        ValidateFile(_file);
        ResultOptions = LoadResultOptions();
        OnPropertyChanged(nameof(ResultOptions));

        AllRecipes.Clear();
        for (int high = 0; high < IngredientCount; high++)
        {
            for (int low = 0; low <= high; low++)
            {
                AllRecipes.Add(new MixRecipeEntry(
                    IngredientOptions[low], IngredientOptions[high], _file, ResultOptions,
                    IngredientOptions));
            }
        }

        ApplyFilter();
        Status = $"Loaded {AllRecipes.Count:N0} recipes using {IngredientCount} ingredients and " +
                 $"{ResultOptions.Count} Mix results.";
    }

    public void ApplyFilter()
    {
        string filter = FilterText.Trim();
        DisplayedRecipes.Clear();
        IEnumerable<MixRecipeEntry> recipes = AllRecipes.Where(recipe =>
            filter.Length == 0 ||
            recipe.Ingredient1Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            recipe.Ingredient2Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            recipe.ResultName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            recipe.ResultDisplayId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            recipe.Ingredient1Index.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            recipe.Ingredient2Index.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (MixRecipeEntry recipe in recipes)
            DisplayedRecipes.Add(recipe);

        Status = $"Showing {DisplayedRecipes.Count:N0} of {AllRecipes.Count:N0} recipes.";
    }

    public void Save()
    {
        ValidateFile(_file);
        foreach (MixRecipeEntry recipe in AllRecipes)
            recipe.Validate(ResultOptions);

        string path = Project_Service.Instance.Path_KernelMixRecipes;
        File.WriteAllBytes(path, _file);
        byte[] verified = File.ReadAllBytes(path);
        ValidateFile(verified);
        if (!_file.SequenceEqual(verified))
            throw new InvalidDataException("The saved Mix recipe file did not verify byte-for-byte.");
        Status = "Saved and verified prepare.bin.";
    }

    public void RestoreOriginalAndSave(string originalPath)
    {
        byte[] original = File.ReadAllBytes(originalPath);
        ValidateFile(original);
        string projectPath = Project_Service.Instance.Path_KernelMixRecipes;
        File.WriteAllBytes(projectPath, original);
        Load();
        Status = "Restored prepare.bin from verified Original Game Files.";
    }

    internal static void ValidateFile(byte[] file)
    {
        if (file.Length != HeaderSize + IngredientCount * RecordSize)
            throw new InvalidDataException(
                $"Unexpected prepare.bin length {file.Length}; expected {HeaderSize + IngredientCount * RecordSize}.");
        if (BitConverter.ToUInt16(file, 0x08) != 0 ||
            BitConverter.ToUInt16(file, 0x0A) != IngredientCount - 1 ||
            BitConverter.ToUInt16(file, 0x0C) != RecordSize ||
            BitConverter.ToInt32(file, 0x10) != HeaderSize)
            throw new InvalidDataException("prepare.bin has an unexpected table header.");

        for (int row = 0; row < IngredientCount; row++)
        {
            for (int column = 0; column < IngredientCount; column++)
            {
                ushort result = BitConverter.ToUInt16(
                    file, HeaderSize + row * RecordSize + column * sizeof(ushort));
                if (column > row)
                {
                    if (result != 0)
                        throw new InvalidDataException("prepare.bin has data in its reserved upper triangle.");
                }
                else if (result < FirstResultId || result > LastResultId)
                    throw new InvalidDataException(
                        $"Recipe [{row},{column}] has invalid result ID 0x{result:X4}.");
            }
        }
    }

    private static IReadOnlyList<MixResultOption> LoadResultOptions()
    {
        string commandPath = Project_Service.Instance.Path_KernelCommandUs;
        List<Ability_Command> commands = Ability_Command.ReadList(
            File.ReadAllBytes(commandPath), hasExtraInfo: true);
        if (commands.Count <= LastResultId - 0x3000)
            throw new InvalidDataException("command.bin does not contain all 64 Mix result commands.");

        return Enumerable.Range(FirstResultId, LastResultId - FirstResultId + 1)
            .Select(id =>
            {
                int commandIndex = id - CommandCategory;
                Ability_Command command = commands[commandIndex];
                string name = FFXProjectEditor.Utils.Encoding.FfxEncoding.DecodeEditableTextScript(
                    command.NameScriptBytes,
                    FFXProjectEditor.Utils.Encoding.FfxEncoding.UsDecoder);
                return new MixResultOption((ushort)id, commandIndex, name);
            }).ToList();
    }

    private static string ItemName(int index) =>
        Item_Dictionary.Instance.TryGetValue((ushort)index, out string? name)
            ? name
            : $"Item {index}";

}

internal sealed class MixRecipeEntry : ObservableObject
{
    private readonly byte[] _file;
    private MixIngredientOption _selectedIngredient1;
    private MixIngredientOption _selectedIngredient2;

    public IReadOnlyList<MixIngredientOption> IngredientOptions { get; }
    public IReadOnlyList<MixResultOption> ResultOptions { get; }
    public int Ingredient1Index => SelectedIngredient1.Index;
    public string Ingredient1Name => SelectedIngredient1.Name;
    public int Ingredient2Index => SelectedIngredient2.Index;
    public string Ingredient2Name => SelectedIngredient2.Name;
    public MixIngredientOption SelectedIngredient1
    {
        get => _selectedIngredient1;
        set
        {
            if (SetProperty(ref _selectedIngredient1, value))
                IngredientSelectionChanged();
        }
    }
    public MixIngredientOption SelectedIngredient2
    {
        get => _selectedIngredient2;
        set
        {
            if (SetProperty(ref _selectedIngredient2, value))
                IngredientSelectionChanged();
        }
    }

    public MixResultOption? SelectedResult
    {
        get
        {
            ushort id = BitConverter.ToUInt16(_file, ResultOffset);
            return ResultOptions.FirstOrDefault(option => option.Id == id);
        }
        set
        {
            int offset = ResultOffset;
            if (value is null || value.Id == BitConverter.ToUInt16(_file, offset))
                return;
            _file[offset] = (byte)value.Id;
            _file[offset + 1] = (byte)(value.Id >> 8);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResultName));
            OnPropertyChanged(nameof(ResultDisplayId));
        }
    }

    public string ResultName => SelectedResult?.Name ?? "Unknown";
    public string ResultDisplayId => $"0x{BitConverter.ToUInt16(_file, ResultOffset):X4}";

    public MixRecipeEntry(MixIngredientOption ingredient1, MixIngredientOption ingredient2,
        byte[] file, IReadOnlyList<MixResultOption> resultOptions,
        IReadOnlyList<MixIngredientOption> ingredientOptions)
    {
        _selectedIngredient1 = ingredient1;
        _selectedIngredient2 = ingredient2;
        _file = file;
        ResultOptions = resultOptions;
        IngredientOptions = ingredientOptions;
    }

    public void Validate(IReadOnlyList<MixResultOption> resultOptions)
    {
        ushort id = BitConverter.ToUInt16(_file, ResultOffset);
        if (id < MixEditor_DataModel.FirstResultId || id > MixEditor_DataModel.LastResultId ||
            !resultOptions.Any(option =>
                option.Id == id &&
                option.CommandIndex == id - MixEditor_DataModel.CommandCategory))
            throw new InvalidDataException($"A recipe refers to invalid Mix result 0x{id:X4}.");
    }

    private int ResultOffset
    {
        get
        {
            int row = Math.Max(Ingredient1Index, Ingredient2Index);
            int column = Math.Min(Ingredient1Index, Ingredient2Index);
            return MixEditor_DataModel.HeaderSize +
                   row * MixEditor_DataModel.RecordSize +
                   column * sizeof(ushort);
        }
    }

    private void IngredientSelectionChanged()
    {
        OnPropertyChanged(nameof(Ingredient1Index));
        OnPropertyChanged(nameof(Ingredient1Name));
        OnPropertyChanged(nameof(Ingredient2Index));
        OnPropertyChanged(nameof(Ingredient2Name));
        OnPropertyChanged(nameof(SelectedResult));
        OnPropertyChanged(nameof(ResultName));
        OnPropertyChanged(nameof(ResultDisplayId));
    }
}

internal sealed record MixResultOption(ushort Id, int CommandIndex, string Name)
{
    public string Display => Name;
}

internal sealed record MixIngredientOption(int Index, string Name)
{
    public string Display => Name;
}
