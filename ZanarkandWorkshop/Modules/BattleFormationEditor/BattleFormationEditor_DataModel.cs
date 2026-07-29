using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.BattleFormation;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.BattleFormationEditor;

public sealed record BattleFormationFileItem(
    string Name,
    string FriendlyName,
    string RelativePath,
    string FullPath)
{
    public override string ToString() => FriendlyName;
}

public sealed record EnemyOption(ushort Id, string Name)
{
    public string Display => Id == ushort.MaxValue ? "(Empty)" : Name;
    public override string ToString() => Display;
}

public partial class EnemySlotRow : ObservableObject
{
    public int SlotNumber { get; }
    [ObservableProperty] private EnemyOption? selectedEnemy;
    public event EventHandler? SelectedEnemyChanged;

    public EnemySlotRow(int slotNumber) => SlotNumber = slotNumber;

    partial void OnSelectedEnemyChanged(EnemyOption? value) =>
        SelectedEnemyChanged?.Invoke(this, EventArgs.Empty);
}

public partial class FormationPositionRow : ObservableObject
{
    private static readonly string[] AeonNames =
    {
        "Valefor", "Ifrit", "Ixion", "Shiva", "Bahamut", "Anima",
        "Yojimbo", "Cindy", "Sandy", "Mindy", "PC Dummy", "PC Dummy 2"
    };

    public FormationPositionKind Kind { get; }
    public int Index { get; }
    public int FileOffset { get; }
    public string Label => Kind switch
    {
        FormationPositionKind.Party => $"Party {Index + 1}",
        FormationPositionKind.PartySecondary => $"Party run-away {Index + 1}",
        FormationPositionKind.Aeon when Index < AeonNames.Length => AeonNames[Index],
        FormationPositionKind.Aeon => $"Aeon {Index + 1}",
        FormationPositionKind.Monster => $"Monster {Index + 1}",
        FormationPositionKind.MonsterSecondary => $"Monster run-away {Index + 1}",
        _ => $"{Kind} {Index + 1}"
    };

    public string Marker =>
        (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string TooltipText => Kind switch
    {
        FormationPositionKind.Party => $"Party position {Index + 1}",
        FormationPositionKind.PartySecondary => $"Party run-away {Index + 1}",
        FormationPositionKind.Aeon when Index < AeonNames.Length =>
            $"{AeonNames[Index]} · Aeon position {Index + 1}",
        FormationPositionKind.Aeon => $"Aeon position {Index + 1}",
        FormationPositionKind.Monster =>
            $"{MonsterName ?? $"Monster {Index + 1}"} · Position",
        FormationPositionKind.MonsterSecondary =>
            $"{MonsterName ?? $"Monster {Index + 1}"} · Run-away position",
        _ => Label
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    private string? monsterName;

    [ObservableProperty] private float x;
    [ObservableProperty] private float y;
    [ObservableProperty] private float z;
    [ObservableProperty] private float w;

    public FormationPositionRow(FormationPosition position)
    {
        Kind = position.Kind;
        Index = position.Index;
        FileOffset = position.FileOffset;
        X = position.X;
        Y = position.Y;
        Z = position.Z;
        W = position.W;
    }

    public FormationPosition ToRecord() =>
        new(Kind, Index, FileOffset, X, Y, Z, W);
}

public partial class BattleFormationEditor_DataModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, string> RegionByBattlePrefix =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["azit"] = "Bikanel", ["bika"] = "Bikanel", ["bjyt"] = "Baaj Temple",
            ["bsil"] = "Besaid", ["bvyt"] = "Bevelle", ["cdsp"] = "Al Bhed Ship",
            ["dome"] = "Zanarkand Ruins", ["genk"] = "Moonflow", ["hiku"] = "Airship",
            ["kami"] = "Thunder Plains", ["kino"] = "Mushroom Rock Road", ["klyt"] = "Kilika",
            ["lchb"] = "Luca", ["lmyt"] = "Calm Lands", ["maca"] = "Lake Macalania",
            ["mcfr"] = "Macalania Woods", ["mcyt"] = "Lake Macalania",
            ["mihn"] = "Mi'ihen Highroad", ["mtgz"] = "Mt. Gagazet", ["nagi"] = "Calm Lands",
            ["omeg"] = "Omega Ruins", ["sins"] = "Sin", ["slik"] = "S.S. Liki",
            ["ssbt"] = "Sin", ["stbv"] = "Bevelle", ["syst"] = "Special",
            ["test"] = "Main Menu", ["tori"] = "Special", ["zkrn"] = "Zanarkand Ruins",
            ["znkd"] = "Dream Zanarkand", ["zzzz"] = "Monster Arena"
        };

    private BattleFormationFile? _loaded;
    private readonly List<BattleFormationFileItem> _allFiles = new();

    public ObservableCollection<BattleFormationFileItem> Files { get; } = new();
    public ObservableCollection<EnemySlotRow> EnemySlots { get; } = new();
    public ObservableCollection<FormationPositionRow> Positions { get; } = new();
    public IReadOnlyList<EnemyOption> EnemyOptions { get; }

    [ObservableProperty] private string filterText = "";
    [ObservableProperty] private BattleFormationFileItem? selectedFile;
    [ObservableProperty] private FormationPositionRow? selectedPosition;
    [ObservableProperty] private string status = "Select a battle file.";
    [ObservableProperty] private string structureSummary = "";
    [ObservableProperty] private bool hasLoadedFile;

    public BattleFormationEditor_DataModel()
    {
        EnemyOptions = BuildEnemyOptions();
        for (int i = 0; i < 8; i++)
        {
            var slot = new EnemySlotRow(i + 1);
            slot.SelectedEnemyChanged += EnemySlotChanged;
            EnemySlots.Add(slot);
        }
        RefreshFiles();
    }

    public void RefreshFiles()
    {
        _allFiles.Clear();
        Files.Clear();
        string root = Project_Service.Instance.Path_Btl;
        if (!Directory.Exists(root))
        {
            Status = $"Battle folder not found: {root}";
            return;
        }

        foreach (string path in Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(root, path);
            string technicalName = Path.GetFileNameWithoutExtension(path);
            _allFiles.Add(new BattleFormationFileItem(
                technicalName, BuildFriendlyName(technicalName, path), relative, path));
        }
        ApplyFilter();
        Status = $"{_allFiles.Count} battle files found. Enemy parties support up to eight monsters.";
    }

    public void ReloadSelected()
    {
        if (SelectedFile is not null)
            Load(SelectedFile);
    }

    public byte[] BuildOutput()
    {
        if (_loaded is null)
            throw new InvalidOperationException("No battle formation is loaded.");

        ushort[] activeIds = EnemySlots
            .Select(slot => slot.SelectedEnemy?.Id ?? ushort.MaxValue)
            .Where(id => id != ushort.MaxValue)
            .ToArray();
        ushort[] ids = activeIds
            .Concat(Enumerable.Repeat(ushort.MaxValue, 8 - activeIds.Length))
            .ToArray();
        return BattleFormationWriter.Write(
            _loaded, ids, Positions.Select(position => position.ToRecord()).ToArray());
    }

    public void Save()
    {
        if (_loaded is null || SelectedFile is null)
            throw new InvalidOperationException("No battle formation is loaded.");

        byte[] output = BuildOutput();
        // Parse our own output before touching the project file.
        BattleFormationParser.Read(output, SelectedFile.FullPath);

        string temporary = SelectedFile.FullPath + ".zwtmp";
        File.WriteAllBytes(temporary, output);
        File.Move(temporary, SelectedFile.FullPath, true);
        Status = $"Saved {SelectedFile.RelativePath}.";
        _loaded = BattleFormationParser.Read(output, SelectedFile.FullPath);
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFileChanged(BattleFormationFileItem? value)
    {
        if (value is not null)
            Load(value);
    }

    private void ApplyFilter()
    {
        string filter = FilterText.Trim();
        Files.Clear();
        foreach (BattleFormationFileItem file in _allFiles.Where(file =>
                     filter.Length == 0 ||
                     file.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     file.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            Files.Add(file);
    }

    private void Load(BattleFormationFileItem file)
    {
        try
        {
            BattleFormationFile parsed = BattleFormationParser.Read(file.FullPath);
            _loaded = parsed;

            for (int i = 0; i < EnemySlots.Count; i++)
            {
                ushort id = parsed.EnemyIds[i];
                EnemySlots[i].SelectedEnemy =
                    EnemyOptions.FirstOrDefault(option => option.Id == id) ??
                    new EnemyOption(id, "Unknown enemy");
            }

            Positions.Clear();
            foreach (FormationPosition position in parsed.Positions)
                Positions.Add(new FormationPositionRow(position));
            UpdateMonsterPositionNames();

            StructureSummary =
                $"Enemies: 8   Party: {parsed.PartyCount} + {parsed.PartyCount} secondary   " +
                $"Aeons: {parsed.AeonCount}   Monsters: {parsed.MonsterCount} + {parsed.MonsterCount} secondary   " +
                $"Header: 0x{parsed.PositionHeaderOffset:X}";
            HasLoadedFile = true;
            Status = $"Loaded {file.RelativePath} ({parsed.OriginalBytes.Length:N0} bytes).";
        }
        catch (Exception ex)
        {
            _loaded = null;
            Positions.Clear();
            StructureSummary = "";
            HasLoadedFile = false;
            Status = $"Could not parse {file.RelativePath}: {ex.Message}";
        }
    }

    private void EnemySlotChanged(object? sender, EventArgs e) =>
        SyncMonsterPositionsToEnemyParty();

    private void SyncMonsterPositionsToEnemyParty()
    {
        if (_loaded is null)
            return;

        int desiredCount = EnemySlots.Count(slot =>
            slot.SelectedEnemy is { Id: not ushort.MaxValue });
        List<FormationPositionRow> current = Positions
            .Where(position => position.Kind == FormationPositionKind.Monster)
            .OrderBy(position => position.Index)
            .ToList();
        List<FormationPositionRow> currentRun = Positions
            .Where(position => position.Kind == FormationPositionKind.MonsterSecondary)
            .OrderBy(position => position.Index)
            .ToList();
        if (current.Count != desiredCount || currentRun.Count != desiredCount)
        {
            foreach (FormationPositionRow row in current.Concat(currentRun).ToArray())
                Positions.Remove(row);

            for (int i = 0; i < desiredCount; i++)
            {
                Positions.Add(i < current.Count
                    ? CopyPosition(current[i], FormationPositionKind.Monster, i)
                    : CreateMonsterPosition(i, current));
            }
            for (int i = 0; i < desiredCount; i++)
            {
                Positions.Add(i < currentRun.Count
                    ? CopyPosition(currentRun[i], FormationPositionKind.MonsterSecondary, i)
                    : CreateMonsterRunPosition(i, currentRun));
            }
        }
        UpdateMonsterPositionNames();
    }

    private FormationPositionRow CreateMonsterPosition(
        int index, IReadOnlyList<FormationPositionRow> existing)
    {
        if (existing.Count > 0)
        {
            FormationPositionRow source = existing[^1];
            return NewPosition(
                FormationPositionKind.Monster, index,
                source.X + 20f * (index - existing.Count + 1), source.Y, source.Z);
        }

        FormationPositionRow[] party = Positions
            .Where(position => position.Kind == FormationPositionKind.Party)
            .ToArray();
        float x = party.Length == 0 ? 0 : party.Average(position => position.X);
        float y = party.Length == 0 ? 0 : party.Average(position => position.Y);
        float z = party.Length == 0 ? 60 : party.Average(position => position.Z) + 60;
        return NewPosition(FormationPositionKind.Monster, index, x, y, z);
    }

    private static FormationPositionRow CreateMonsterRunPosition(
        int index, IReadOnlyList<FormationPositionRow> existing)
    {
        if (existing.Count > 0)
        {
            FormationPositionRow source = existing[^1];
            return NewPosition(
                FormationPositionKind.MonsterSecondary, index,
                source.X, source.Y, source.Z);
        }
        return NewPosition(FormationPositionKind.MonsterSecondary, index, 90, 0, -60);
    }

    private static FormationPositionRow CopyPosition(
        FormationPositionRow source, FormationPositionKind kind, int index) =>
        NewPosition(kind, index, source.X, source.Y, source.Z, source.W, source.FileOffset);

    private static FormationPositionRow NewPosition(
        FormationPositionKind kind, int index, float x, float y, float z,
        float w = 0, int fileOffset = -1) =>
        new(new FormationPosition(kind, index, fileOffset, x, y, z, w));

    private void UpdateMonsterPositionNames()
    {
        foreach (FormationPositionRow position in Positions.Where(position =>
                     position.Kind is FormationPositionKind.Monster or
                         FormationPositionKind.MonsterSecondary))
        {
            EnemyOption[] activeEnemies = EnemySlots
                .Select(slot => slot.SelectedEnemy)
                .Where(enemy => enemy is { Id: not ushort.MaxValue })
                .Cast<EnemyOption>()
                .ToArray();
            EnemyOption? enemy = position.Index < activeEnemies.Length
                ? activeEnemies[position.Index]
                : null;
            position.MonsterName = enemy is null || enemy.Id == ushort.MaxValue
                ? $"Empty slot {position.Index + 1}"
                : enemy.Name;
        }
    }

    private static IReadOnlyList<EnemyOption> BuildEnemyOptions()
    {
        var options = new List<EnemyOption>
        {
            new(ushort.MaxValue, "(Empty)")
        };
        options.AddRange(Monster_Dictionary.Instance
            .OrderBy(pair => pair.Key)
            .Select(pair => new EnemyOption(
                checked((ushort)(0x1000 + pair.Key)), pair.Value)));
        return options;
    }

    private static string BuildFriendlyName(string technicalName, string path)
    {
        string prefix = technicalName.Length >= 4 ? technicalName[..4] : technicalName;
        string region = RegionByBattlePrefix.TryGetValue(prefix, out string? knownRegion)
            ? knownRegion
            : "Unknown area";

        if (!string.Equals(region, "Monster Arena", StringComparison.Ordinal))
            return $"{region} - {technicalName}";

        try
        {
            BattleFormationFile formation = BattleFormationParser.Read(path);
            var names = formation.EnemyIds
                .Where(id => id != ushort.MaxValue)
                .Select(id =>
                {
                    int monsterId = id - 0x1000;
                    return Monster_Dictionary.Instance.TryGetValue((short)monsterId, out string? name)
                        ? name
                        : $"Enemy {id:X4}";
                })
                .GroupBy(name => name)
                .Select(group => new { Name = group.Key, Count = group.Count() })
                .ToList();

            if (names.Count == 0)
                return $"{region} — Empty formation";

            string lineup = string.Join(" + ", names.Take(3)
                .Select(enemy => enemy.Count > 1
                    ? $"{enemy.Name} ×{enemy.Count}"
                    : enemy.Name));
            if (names.Count > 3)
                lineup += $" + {names.Count - 3} more";
            return $"{region} — {lineup}";
        }
        catch
        {
            return $"{region} — Special battle";
        }
    }
}
