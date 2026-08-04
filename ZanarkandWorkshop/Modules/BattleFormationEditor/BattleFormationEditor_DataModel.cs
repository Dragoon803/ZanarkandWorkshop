using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.BattleFormation;
using FFXProjectEditor.FfxLib.Battlefield;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.ComponentModel;

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

    public string DisplayName => Kind switch
    {
        FormationPositionKind.Monster => MonsterName ?? Label,
        FormationPositionKind.MonsterSecondary => $"{MonsterName ?? $"Monster {Index + 1}"} — Run-away",
        _ => Label
    };

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
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string? monsterName;

    [ObservableProperty] private float x;
    [ObservableProperty] private float y;
    [ObservableProperty] private float z;
    [ObservableProperty] private float w;

    private float _loadedX;
    private float _loadedY;
    private float _loadedZ;
    private float _loadedW;

    public bool CanReset =>
        X != _loadedX || Y != _loadedY || Z != _loadedZ || W != _loadedW;

    public FormationPositionRow(FormationPosition position)
    {
        Kind = position.Kind;
        Index = position.Index;
        FileOffset = position.FileOffset;
        X = position.X;
        Y = position.Y;
        Z = position.Z;
        W = position.W;
        _loadedX = position.X;
        _loadedY = position.Y;
        _loadedZ = position.Z;
        _loadedW = position.W;
    }

    partial void OnXChanged(float value) => OnPropertyChanged(nameof(CanReset));
    partial void OnYChanged(float value) => OnPropertyChanged(nameof(CanReset));
    partial void OnZChanged(float value) => OnPropertyChanged(nameof(CanReset));
    partial void OnWChanged(float value) => OnPropertyChanged(nameof(CanReset));

    public void ResetPosition()
    {
        X = _loadedX;
        Y = _loadedY;
        Z = _loadedZ;
        W = _loadedW;
    }

    public void AcceptCurrentPosition()
    {
        _loadedX = X;
        _loadedY = Y;
        _loadedZ = Z;
        _loadedW = W;
        OnPropertyChanged(nameof(CanReset));
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
    private bool _isLoading;
    private int _lastActiveEnemyCount;
    private ushort[] _lastEnemyIds = Enumerable.Repeat(ushort.MaxValue, 8).ToArray();
    private readonly List<BattleFormationFileItem> _allFiles = new();
    private IReadOnlyList<BattlefieldAsset> _battlefieldAssets = [];
    private BattlefieldFormationIndex? _battlefieldIndex;

    public ObservableCollection<BattleFormationFileItem> Files { get; } = new();
    public ObservableCollection<EnemySlotRow> EnemySlots { get; } = new();
    public ObservableCollection<FormationPositionRow> Positions { get; } = new();
    public ObservableCollection<FormationPositionRow> GridPositions { get; } = new();
    public IReadOnlyList<EnemyOption> EnemyOptions { get; }

    [ObservableProperty] private string filterText = "";
    [ObservableProperty] private BattleFormationFileItem? selectedFile;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPosition))]
    [NotifyPropertyChangedFor(nameof(CanResetSelectedPosition))]
    private FormationPositionRow? selectedPosition;
    [ObservableProperty] private string status = "Select a battle file.";
    [ObservableProperty] private string structureSummary = "";
    [ObservableProperty] private bool hasLoadedFile;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private BattlefieldHeightMap? battlefield;
    [ObservableProperty] private string battlefieldStatus = "No battlefield surface loaded.";
    [ObservableProperty] private bool showBattlefieldNote;
    [ObservableProperty] private string battlefieldNote = "";
    private string _battlefieldIndexStatus = "Battlefield data has not been checked.";
    public int ActiveEnemyCount => EnemySlots.Count(slot =>
        slot.SelectedEnemy is { Id: not ushort.MaxValue });
    public bool HasSelectedPosition => SelectedPosition is not null;
    public bool CanResetSelectedPosition => SelectedPosition?.CanReset == true;

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
        RefreshBattlefieldIndex();
        if (!Directory.Exists(root))
        {
            Status = $"Battle folder not found: {root}";
            return;
        }

        foreach (string path in Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            BattleFormationFile parsed;
            try { parsed = BattleFormationParser.Read(path); }
            catch { continue; }
            int activeMonsterCount = parsed.EnemyIds.Count(id => id != ushort.MaxValue);
            bool hasDisplayablePosition = parsed.Positions.Any(position =>
                position.Kind is not FormationPositionKind.Monster and
                    not FormationPositionKind.MonsterSecondary ||
                position.Index < activeMonsterCount);
            if (parsed.MonsterCount is < 1 or > 8 ||
                activeMonsterCount == 0 ||
                !hasDisplayablePosition)
                continue;
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
        if (activeIds.Length == 0)
            throw new InvalidDataException("At least one monster slot must be filled before saving.");
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
        Status = EditorSaveStatus.Success("Battle Formation");
        _loaded = BattleFormationParser.Read(output, SelectedFile.FullPath);
        foreach (FormationPositionRow position in Positions)
            position.AcceptCurrentPosition();
        IsDirty = false;
        OnPropertyChanged(nameof(CanResetSelectedPosition));
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFileChanged(BattleFormationFileItem? value)
    {
        if (value is not null)
            Load(value);
    }

    partial void OnSelectedPositionChanged(FormationPositionRow? value) =>
        OnPropertyChanged(nameof(CanResetSelectedPosition));

    public void ResetSelectedPosition()
    {
        if (SelectedPosition?.CanReset != true)
            return;
        SelectedPosition.ResetPosition();
        OnPropertyChanged(nameof(CanResetSelectedPosition));
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
            _isLoading = true;
            BattleFormationFile parsed = BattleFormationParser.Read(file.FullPath);
            _loaded = parsed;

            for (int i = 0; i < EnemySlots.Count; i++)
            {
                ushort id = parsed.EnemyIds[i];
                EnemySlots[i].SelectedEnemy =
                    EnemyOptions.FirstOrDefault(option => option.Id == id) ??
                    new EnemyOption(id, "Unknown enemy");
            }
            _lastActiveEnemyCount = EnemySlots.Count(slot =>
                slot.SelectedEnemy is { Id: not ushort.MaxValue });
            _lastEnemyIds = EnemySlots.Select(slot =>
                slot.SelectedEnemy?.Id ?? ushort.MaxValue).ToArray();

            foreach (FormationPositionRow oldRow in Positions)
                oldRow.PropertyChanged -= PositionChanged;
            Positions.Clear();
            foreach (FormationPosition position in parsed.Positions)
            {
                var row = new FormationPositionRow(position);
                row.PropertyChanged += PositionChanged;
                Positions.Add(row);
            }
            UpdateMonsterPositionNames();
            RefreshGridPositions();
            LoadBattlefield(file.Name);
            if (GridPositions.Count == 0)
            {
                ShowBattlefieldNote = true;
                BattlefieldNote = "This battle does not contain position data to display.";
            }

            StructureSummary =
                $"Enemies: 8   Party: {parsed.PartyCount} + {parsed.PartyCount} secondary   " +
                $"Aeons: {parsed.AeonCount}   Monsters: {parsed.MonsterCount} + {parsed.MonsterCount} secondary   " +
                $"Header: 0x{parsed.PositionHeaderOffset:X}";
            HasLoadedFile = true;
            IsDirty = false;
            Status = $"Loaded {file.RelativePath} ({parsed.OriginalBytes.Length:N0} bytes).";
            OnPropertyChanged(nameof(ActiveEnemyCount));
        }
        catch (Exception ex)
        {
            _loaded = null;
            Positions.Clear();
            GridPositions.Clear();
            Battlefield = null;
            BattlefieldStatus = "No battlefield surface loaded.";
            ShowBattlefieldNote = false;
            BattlefieldNote = "";
            StructureSummary = "";
            HasLoadedFile = false;
            IsDirty = false;
            Status = $"Could not parse {file.RelativePath}: {ex.Message}";
        }
        finally { _isLoading = false; }
    }

    private void RefreshBattlefieldIndex()
    {
        _battlefieldAssets = [];
        _battlefieldIndex = null;
        string? projectRoot = Project_Service.Instance.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            _battlefieldIndexStatus = "No project is loaded.";
            return;
        }
        try
        {
            string assetRoot = projectRoot;
            if (!Directory.Exists(Path.Combine(assetRoot, "jppc", "btlmap")) &&
                VanillaReference_Service.IsConfigured &&
                !string.IsNullOrWhiteSpace(VanillaReference_Service.MasterPath))
            {
                assetRoot = VanillaReference_Service.MasterPath;
            }
            _battlefieldAssets = BattlefieldAssetCatalog.Discover(assetRoot);

            string relativeBattleList = Path.Combine("jppc", "battle", "kernel", "btl.bin");
            string battleListPath = Path.Combine(projectRoot, relativeBattleList);
            if (!File.Exists(battleListPath) &&
                VanillaReference_Service.IsConfigured &&
                !string.IsNullOrWhiteSpace(VanillaReference_Service.MasterPath))
            {
                battleListPath = Path.Combine(VanillaReference_Service.MasterPath, relativeBattleList);
            }
            if (File.Exists(battleListPath))
                _battlefieldIndex = BattlefieldFormationIndex.Read(battleListPath);

            _battlefieldIndexStatus = _battlefieldAssets.Count == 0
                ? "Battlefield files were not found in the project or configured Original Game Files."
                : _battlefieldIndex is null
                    ? "The battlefield assignment table (jppc\\battle\\kernel\\btl.bin) was not found."
                    : $"{_battlefieldAssets.Count} battlefield surfaces are available.";
        }
        catch (Exception ex)
        {
            _battlefieldAssets = [];
            _battlefieldIndex = null;
            _battlefieldIndexStatus = $"Battlefield data could not be indexed: {ex.Message}";
        }
    }

    private void LoadBattlefield(string formationName)
    {
        Battlefield = null;
        ShowBattlefieldNote = false;
        BattlefieldNote = "";
        BattlefieldStatus = "Drag a point to move it. Drag empty grid space to pan.";
        BattlefieldAsset? asset = _battlefieldIndex?.ResolveAsset(formationName, _battlefieldAssets);
        if (asset is null)
        {
            ShowBattlefieldNote = true;
            BattlefieldNote = _battlefieldAssets.Count == 0 || _battlefieldIndex is null
                ? "Battlefield preview files could not be found."
                : "This battle does not have a separate battlefield preview.";
            BattlefieldStatus += _battlefieldAssets.Count == 0 || _battlefieldIndex is null
                ? $" {_battlefieldIndexStatus}"
                : " No dedicated battlefield surface is mapped to this formation.";
            return;
        }
        try
        {
            if (!BattlefieldHeightMap.TryRead(asset.MapPath, out BattlefieldHeightMap? surface))
            {
                ShowBattlefieldNote = true;
                BattlefieldNote = "This battle does not include a battlefield area to display.";
                BattlefieldStatus += $" {asset.Code} does not contain a collision surface.";
                return;
            }
            Battlefield = surface;
            BattlefieldStatus += $" Battlefield: {asset.Code}.";
        }
        catch (Exception ex)
        {
            ShowBattlefieldNote = true;
            BattlefieldNote = "The battlefield preview could not be loaded.";
            BattlefieldStatus += $" Battlefield could not be loaded: {ex.Message}";
        }
    }

    private void EnemySlotChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        int activeCount = EnemySlots.Count(slot =>
            slot.SelectedEnemy is { Id: not ushort.MaxValue });
        if (activeCount != _lastActiveEnemyCount && _loaded is { CanResizeMonsterTables: false })
        {
            _isLoading = true;
            for (int i = 0; i < EnemySlots.Count; i++)
            {
                ushort previousId = _lastEnemyIds[i];
                EnemySlots[i].SelectedEnemy = EnemyOptions.FirstOrDefault(option => option.Id == previousId) ??
                    new EnemyOption(previousId, "Unknown enemy");
            }
            _isLoading = false;
            Status = "This retail formation has a fixed-size layout. Replace existing monsters without adding or removing slots.";
            return;
        }
        if (activeCount != _lastActiveEnemyCount)
        {
            _lastActiveEnemyCount = activeCount;
            if (activeCount > 0)
                SyncMonsterPositionsToEnemyParty();
        }
        else
        {
            // Some retail formations intentionally contain more position pairs than
            // active enemy IDs. A like-for-like monster replacement must preserve
            // that original structure instead of normalizing it.
            UpdateMonsterPositionNames();
        }
        RefreshGridPositions();
        _lastEnemyIds = EnemySlots.Select(slot =>
            slot.SelectedEnemy?.Id ?? ushort.MaxValue).ToArray();
        OnPropertyChanged(nameof(ActiveEnemyCount));
        RefreshDirtyState();
    }

    private void PositionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedPosition))
            OnPropertyChanged(nameof(CanResetSelectedPosition));
        RefreshDirtyState();
    }

    private void RefreshDirtyState()
    {
        if (_isLoading || _loaded is null) return;
        if (ActiveEnemyCount == 0)
        {
            IsDirty = true;
            return;
        }
        IsDirty = !BuildOutput().SequenceEqual(_loaded.OriginalBytes);
    }

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

            foreach (FormationPositionRow row in Positions)
            {
                row.PropertyChanged -= PositionChanged;
                row.PropertyChanged += PositionChanged;
            }
        }
        UpdateMonsterPositionNames();
    }

    private void RefreshGridPositions()
    {
        int activeMonsterCount = EnemySlots.Count(slot =>
            slot.SelectedEnemy is { Id: not ushort.MaxValue });
        FormationPositionRow? preserve = SelectedPosition;
        GridPositions.Clear();
        foreach (FormationPositionRow position in Positions.Where(position =>
                     position.Kind is not FormationPositionKind.Monster and not FormationPositionKind.MonsterSecondary ||
                     position.Index < activeMonsterCount))
            GridPositions.Add(position);
        if (preserve is not null && !GridPositions.Contains(preserve))
            SelectedPosition = null;
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
            .OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(pair => pair.Key)
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
