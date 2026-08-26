using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public sealed class TreasureFieldItem : ObservableObject
{
    public FieldMapAsset Asset { get; }
    public string LocationName { get; }
    private int? _chestCount;
    public int? ChestCount { get => _chestCount; set { if (SetProperty(ref _chestCount, value)) OnPropertyChanged(nameof(Display)); } }
    public string Display => ChestCount.HasValue
        ? $"{LocationName}  ·  {ChestCount} chest{(ChestCount == 1 ? "" : "s")}  ({Asset.FieldId})"
        : $"{LocationName}  ({Asset.FieldId})";
    public TreasureFieldItem(FieldMapAsset asset)
    {
        Asset = asset;
        LocationName = TreasureFieldNameLookup.GetDisplayName(asset.FieldId, asset.AreaId);
    }
}

public partial class TreasureChestRow : ObservableObject
{
    private readonly string _masterPath;
    private bool _translating;
    public ProjectedChestLocation Location { get; }
    public int TreasureId => ActiveReward?.TreasureId ?? -1;
    public string Label => Location.TreasureIds.Count == 1 ? $"Chest #{Location.TreasureIds[0]}" :
        Location.FieldId.Equals("kami04", StringComparison.OrdinalIgnoreCase)
            ? "Thunder Plains reward chest" : "Conditional chest";
    public string Confidence => Location.Confidence.ToString();
    public string PositionText => Location.WorldPosition is null ? "Unknown" :
        $"X {Location.WorldPosition.X:0.###}, Y {Location.WorldPosition.Y:0.###}, Z {Location.WorldPosition.Z:0.###}";
    public string Provenance => Location.Evidence;
    public bool CanEditContents => ActiveReward is not null;
    public bool HasMultipleRewards => AvailableRewards.Count > 1;
    public ObservableCollection<NpcTreasureRow> AvailableRewards { get; } = [];
    private NpcTreasureRow? _activeReward;
    public NpcTreasureRow? ActiveReward
    {
        get => _activeReward;
        set
        {
            // A two-way ComboBox binding briefly publishes null while its parent
            // SelectedChest binding moves to another row. Do not let that
            // transient UI state erase a valid reward from the new chest.
            if (value is null && AvailableRewards.Count > 0) return;
            if (!SetProperty(ref _activeReward, value)) return;
            OnPropertyChanged(nameof(TreasureId));
            OnPropertyChanged(nameof(FriendlyContents));
        }
    }
    public ObservableCollection<TreasureRewardOption> RewardOptions { get; } = [];
    public IReadOnlyList<TreasureKind> KindOptions { get; } = Enum.GetValues<TreasureKind>();
    public string AmountLabel => SelectedKind == TreasureKind.Gil ? "Gil x100" : "Quantity";
    public string FriendlyContents => ActiveReward?.FriendlyContents ?? "No editable reward";

    private TreasureKind _selectedKind;
    public TreasureKind SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (!SetProperty(ref _selectedKind, value)) return;
            RawKind = (byte)value;
            RefreshRewardOptions(Type, false);
            OnPropertyChanged(nameof(AmountLabel));
            OnPropertyChanged(nameof(FriendlyContents));
        }
    }

    private TreasureRewardOption? _selectedReward;
    public TreasureRewardOption? SelectedReward
    {
        get => _selectedReward;
        set
        {
            if (!SetProperty(ref _selectedReward, value) || value is null || _translating) return;
            Type = value.EncodedId;
            OnPropertyChanged(nameof(FriendlyContents));
        }
    }

    [ObservableProperty] private byte rawKind;
    [ObservableProperty] private byte quantity;
    [ObservableProperty] private ushort type;
    public event EventHandler? Edited;

    public TreasureChestRow(ProjectedChestLocation location, TreasureRecord? record, string masterPath)
    {
        _masterPath = masterPath;
        Location = location;
        if (record is not null)
        {
            _translating = true;
            RawKind = record.RawKind; Quantity = record.Quantity; Type = record.Type;
            _selectedKind = record.Kind ?? (TreasureKind)record.RawKind;
            RefreshRewardOptions(record.Type, true);
            _translating = false;
        }
    }

    public void SetAvailableRewards(IEnumerable<TreasureRecord> records)
    {
        foreach (NpcTreasureRow reward in AvailableRewards) reward.Edited -= AvailableRewardEdited;
        AvailableRewards.Clear();
        foreach (TreasureRecord record in records)
        {
            var reward = new NpcTreasureRow(record.Id, record, _masterPath,
                ThunderRewardCondition(Location.FieldId, record.Id));
            reward.Edited += AvailableRewardEdited;
            AvailableRewards.Add(reward);
        }
        ActiveReward = AvailableRewards.FirstOrDefault();
        OnPropertyChanged(nameof(HasMultipleRewards));
        OnPropertyChanged(nameof(CanEditContents));
    }

    private void AvailableRewardEdited(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FriendlyContents));
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private static string? ThunderRewardCondition(string fieldId, int treasureId)
    {
        if (!fieldId.Equals("kami04", StringComparison.OrdinalIgnoreCase)) return null;
        return treasureId switch
        {
            195 => "Hit 30 times", 196 => "Hit 80 times",
            189 => "Dodge 5 times", 190 => "Dodge 10 times", 191 => "Dodge 20 times",
            192 => "Dodge 50 times", 193 => "Dodge 100 times", 194 => "Dodge 150 times",
            278 => "Dodge 200 times", _ => null
        };
    }

    private void RefreshRewardOptions(ushort preserveId, bool preserveUnknown)
    {
        _translating = true;
        RewardOptions.Clear();
        foreach (TreasureRewardOption option in TreasureRewardLookup.Build(SelectedKind, _masterPath)) RewardOptions.Add(option);
        TreasureRewardOption? selected = RewardOptions.FirstOrDefault(option => option.EncodedId == preserveId);
        if (selected is null && preserveUnknown)
        {
            selected = new TreasureRewardOption(preserveId, $"Unknown / modded ID 0x{preserveId:X4}");
            RewardOptions.Insert(0, selected);
        }
        selected ??= RewardOptions.FirstOrDefault();
        SelectedReward = selected;
        if (selected is not null && !preserveUnknown) Type = selected.EncodedId;
        _translating = false;
    }

    partial void OnRawKindChanged(byte value) => Edited?.Invoke(this, EventArgs.Empty);
    partial void OnQuantityChanged(byte value) { OnPropertyChanged(nameof(FriendlyContents)); Edited?.Invoke(this, EventArgs.Empty); }
    partial void OnTypeChanged(ushort value) { OnPropertyChanged(nameof(FriendlyContents)); Edited?.Invoke(this, EventArgs.Empty); }
}

public sealed class NpcTreasureRow : ObservableObject
{
    private readonly string _masterPath;
    private bool _translating;
    public int TreasureId { get; }
    public ObservableCollection<TreasureRewardOption> RewardOptions { get; } = [];
    public IReadOnlyList<TreasureKind> KindOptions { get; } = Enum.GetValues<TreasureKind>();
    private readonly string? _displayPrefix;
    public string Display => string.IsNullOrWhiteSpace(_displayPrefix)
        ? $"#{TreasureId} · {FriendlyContents}"
        : $"{_displayPrefix} — {FriendlyContents}";
    public string AmountLabel => SelectedKind == TreasureKind.Gil ? "Gil x100" : "Quantity";
    public string FriendlyContents => TreasureRewardLookup.Describe(SelectedKind, Quantity, Type, _masterPath);

    private TreasureKind _selectedKind;
    public TreasureKind SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (!SetProperty(ref _selectedKind, value)) return;
            RawKind = (byte)value;
            RefreshRewardOptions(Type, false);
            RefreshDisplay();
        }
    }

    private TreasureRewardOption? _selectedReward;
    public TreasureRewardOption? SelectedReward
    {
        get => _selectedReward;
        set
        {
            if (!SetProperty(ref _selectedReward, value) || value is null || _translating) return;
            Type = value.EncodedId;
            RefreshDisplay();
        }
    }

    private byte _rawKind;
    public byte RawKind { get => _rawKind; private set { if (SetProperty(ref _rawKind, value)) Edited?.Invoke(this, EventArgs.Empty); } }
    private byte _quantity;
    public byte Quantity { get => _quantity; set { if (SetProperty(ref _quantity, value)) { RefreshDisplay(); Edited?.Invoke(this, EventArgs.Empty); } } }
    private ushort _type;
    public ushort Type { get => _type; private set { if (SetProperty(ref _type, value)) { RefreshDisplay(); Edited?.Invoke(this, EventArgs.Empty); } } }
    public event EventHandler? Edited;

    public NpcTreasureRow(int treasureId, TreasureRecord record, string masterPath, string? displayPrefix = null)
    {
        TreasureId = treasureId;
        _masterPath = masterPath;
        _displayPrefix = displayPrefix;
        _translating = true;
        _rawKind = record.RawKind;
        _quantity = record.Quantity;
        _type = record.Type;
        _selectedKind = record.Kind ?? (TreasureKind)record.RawKind;
        RefreshRewardOptions(record.Type, true);
        _translating = false;
    }

    public void ApplyRecord(TreasureRecord record)
    {
        if (record.Id != TreasureId)
            throw new InvalidOperationException("The restored treasure record has a different ID.");
        _translating = true;
        _rawKind = record.RawKind;
        _quantity = record.Quantity;
        _type = record.Type;
        _selectedKind = record.Kind ?? (TreasureKind)record.RawKind;
        OnPropertyChanged(nameof(RawKind));
        OnPropertyChanged(nameof(Quantity));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(SelectedKind));
        RefreshRewardOptions(record.Type, true);
        _translating = false;
        RefreshDisplay();
    }

    private void RefreshRewardOptions(ushort preserveId, bool preserveUnknown)
    {
        _translating = true;
        RewardOptions.Clear();
        foreach (TreasureRewardOption option in TreasureRewardLookup.Build(SelectedKind, _masterPath))
            RewardOptions.Add(option);
        TreasureRewardOption? selected = RewardOptions.FirstOrDefault(option => option.EncodedId == preserveId);
        if (selected is null && preserveUnknown)
        {
            selected = new TreasureRewardOption(preserveId, $"Unknown / modded ID 0x{preserveId:X4}");
            RewardOptions.Insert(0, selected);
        }
        selected ??= RewardOptions.FirstOrDefault();
        SelectedReward = selected;
        if (selected is not null && !preserveUnknown) Type = selected.EncodedId;
        _translating = false;
    }

    private void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(AmountLabel));
        OnPropertyChanged(nameof(FriendlyContents));
    }
}

public partial class TreasureMapEditor_DataModel : ObservableObject
{
    private TreasureMapIndex _index;
    private ChestLocationIndex? _locations;
    private GuideMapGeometry? _currentGeometry;
    private TreasureMapIndex? _currentFieldIndex;
    private bool _loading;
    private readonly Dictionary<int, TreasureRecord> _edits = [];
    private readonly List<TreasureFieldItem> _allFields = [];
    private readonly List<TreasureChestRow> _modelChests = [];
    private readonly SemaphoreSlim _fieldLoadGate = new(1, 1);
    private CancellationTokenSource? _fieldLoadCancellation;
    private int _memoryReleasePending;
    private int _fieldLoadGeneration;
    public ObservableCollection<TreasureFieldItem> Fields { get; } = [];
    public ObservableCollection<TreasureChestRow> Chests { get; } = [];
    public ObservableCollection<TreasureChestRow> MapChests { get; } = [];
    public ObservableCollection<NpcTreasureRow> NpcRewards { get; } = [];
    public bool HasNoNpcRewards => NpcRewards.Count == 0;
    public IReadOnlyList<TreasureKind> TreasureKinds { get; } = Enum.GetValues<TreasureKind>();
    public string CatalogPath => _index.Catalog.Path;

    [ObservableProperty] private TreasureFieldItem? selectedField;
    [ObservableProperty] private TreasureChestRow? selectedChest;
    [ObservableProperty] private NpcTreasureRow? selectedNpcReward;
    [ObservableProperty] private int selectedModelIndex;
    [ObservableProperty] private string status = "Scanning game files…";
    [ObservableProperty] private string historyStatus = "";
    [ObservableProperty] private bool isDirty;
    private readonly Stack<Dictionary<int, TreasureRecord>> _undoHistory = new();
    private readonly Stack<Dictionary<int, TreasureRecord>> _redoHistory = new();
    public bool CanUndo => _undoHistory.Count > 0;
    public bool CanRedo => _redoHistory.Count > 0;
    public bool CanUndoAll => IsDirty;
    [ObservableProperty] private string fieldFilterText = "";
    [ObservableProperty] private bool isFieldLoading;
    public bool IsApplyingFieldFilter { get; private set; }
    [ObservableProperty] private string emptyStateText = "Select a field to load its map and chest data.";

    public GuideMapModel? CurrentModel => _currentGeometry is null || _currentGeometry.Models.Count == 0
        ? null : _currentGeometry.Models[Math.Clamp(SelectedModelIndex, 0, _currentGeometry.Models.Count - 1)];
    public bool HasCurrentModel => CurrentModel is not null;
    public int ModelCount => _currentGeometry?.Models.Count ?? 0;
    public bool HasMultipleMapStates => ModelCount > 1;
    public bool CanPreviousMapState => HasMultipleMapStates && SelectedModelIndex > 0;
    public bool CanNextMapState => HasMultipleMapStates && SelectedModelIndex < ModelCount - 1;
    public string MapStateNavigationText => HasMultipleMapStates
        ? $"Map {SelectedModelIndex + 1} of {ModelCount}"
        : "";
    public string ModelText => IsFieldLoading ? "Loading…" : ModelCount == 0 ? "No map loaded" : $"Map state {SelectedModelIndex + 1} of {ModelCount}";
    public int DetectedChestCount => _locations?.WorkerCount ?? 0;
    public int PlacedChestCount => _locations is null ? 0 : _locations.Locations
        .Where(location => location.ModelIndex.HasValue)
        .Select(location => (location.EventId, location.WorkerIndex))
        .Distinct()
        .Count();
    public int UnplacedChestCount => Math.Max(0, DetectedChestCount - PlacedChestCount);
    public bool HasPlacementNotice => UnplacedChestCount > 0;
    public string PlacementNotice => !HasPlacementNotice ? "" :
        $"{PlacedChestCount} of {DetectedChestCount} detected chest workers have recoverable map positions. " +
        $"{UnplacedChestCount} chest{(UnplacedChestCount == 1 ? "" : "s")} cannot currently be shown as an icon.";
    public string ChestNavigationText
    {
        get
        {
            int index = SelectedChest is null ? -1 : Chests.IndexOf(SelectedChest);
            return index < 0 ? "No chests" : $"Chest {index + 1} of {Chests.Count}";
        }
    }
    public bool CanPreviousChest => SelectedChest is not null && Chests.IndexOf(SelectedChest) > 0;
    public bool CanNextChest
    {
        get
        {
            int index = SelectedChest is null ? -1 : Chests.IndexOf(SelectedChest);
            return index >= 0 && index < Chests.Count - 1;
        }
    }

    public TreasureMapEditor_DataModel(Action<string>? progress = null)
    {
        string master = Project_Service.Instance.ProjectPath ?? throw new InvalidOperationException("No project is loaded.");
        TreasureMapPrerequisiteResult prerequisites = TreasureMapPrerequisites.Validate(master);
        if (!prerequisites.IsValid) throw new System.IO.InvalidDataException(prerequisites.Message);
        progress?.Invoke("Reading the field directory and treasure catalog…");
        TreasureCatalog catalog = TreasureCatalog.Read(System.IO.Path.Combine(master, "jppc", "battle", "kernel", "takara.bin"));
        IReadOnlyList<FieldMapAsset> assets = FieldAssetDiscovery.ScanMaster(master);
        _index = new TreasureMapIndex(catalog, assets, [], []);
        foreach (FieldMapAsset asset in assets.Where(asset =>
                     asset.HasEvents && TreasureFieldManifest.TryGetChestCount(asset.FieldId, out _)))
        {
            TreasureFieldManifest.TryGetChestCount(asset.FieldId, out int count);
            _allFields.Add(new TreasureFieldItem(asset) { ChestCount = count });
        }
        ApplyFieldFilter();
        SelectedField = null;
        Status = $"{Fields.Count} known chest fields available. Select one to load it.";
    }

    partial void OnFieldFilterTextChanged(string value) => ApplyFieldFilter();
    partial void OnSelectedChestChanged(TreasureChestRow? value) => RefreshChestNavigation();

    private void RefreshChestNavigation()
    {
        OnPropertyChanged(nameof(ChestNavigationText));
        OnPropertyChanged(nameof(CanPreviousChest));
        OnPropertyChanged(nameof(CanNextChest));
    }

    private void ApplyFieldFilter()
    {
        TreasureFieldItem? preserve = SelectedField;
        string filter = FieldFilterText.Trim();
        List<TreasureFieldItem> desired = _allFields.Where(field =>
            string.IsNullOrWhiteSpace(filter) ||
            field.Display.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            ReferenceEquals(field, preserve)).ToList();

        IsApplyingFieldFilter = true;
        try
        {
            // Reconcile the visible list without clearing it. Clearing briefly drops
            // the ListBox selection and can unload a dirty field before its pending
            // changes have been resolved.
            for (int index = 0; index < desired.Count; index++)
            {
                TreasureFieldItem field = desired[index];
                int existingIndex = Fields.IndexOf(field);
                if (existingIndex < 0)
                    Fields.Insert(index, field);
                else if (existingIndex != index)
                    Fields.Move(existingIndex, index);
            }

            for (int index = Fields.Count - 1; index >= desired.Count; index--)
                Fields.RemoveAt(index);

            if (preserve is not null && Fields.Contains(preserve))
                SelectedField = preserve;
            else if (preserve is not null)
                SelectedField = null;
        }
        finally
        {
            IsApplyingFieldFilter = false;
        }
    }

    partial void OnSelectedFieldChanged(TreasureFieldItem? value)
    {
        // History describes the currently loaded map. A clean map can still have
        // future Redo entries after Undo; do not carry that invisible timeline
        // into a different field.
        if (!IsDirty)
            ClearHistoryForFieldChange();
        BeginLoadField(value);
    }

    private void ClearHistoryForFieldChange()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        HistoryStatus = "";
        NotifyHistoryState();
    }
    partial void OnSelectedModelIndexChanged(int value)
    {
        LoadChests();
        if (!IsFieldLoading && _currentGeometry is not null)
            EmptyStateText = MapChests.Count == 0 ? "No positioned chest icons match this map state." : "";
        OnPropertyChanged(nameof(CurrentModel)); OnPropertyChanged(nameof(HasCurrentModel)); OnPropertyChanged(nameof(ModelText));
        NotifyMapStateNavigation();
    }

    private async void BeginLoadField(TreasureFieldItem? field)
    {
        _fieldLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _fieldLoadCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        int generation = ++_fieldLoadGeneration;
        bool releasedLargeField = _currentGeometry is not null || _currentFieldIndex is not null || _locations is not null;
        SelectedModelIndex = 0;
        _currentGeometry = null;
        _currentFieldIndex = null;
        _locations = null;
        ClearChests();
        ClearNpcRewards();
        NotifyMapChanged();
        NotifyPlacementChanged();
        if (releasedLargeField)
            Interlocked.Exchange(ref _memoryReleasePending, 1);
        if (field is null)
        {
            IsFieldLoading = false;
            EmptyStateText = "Select a field to load its map and chest data.";
            if (ReferenceEquals(_fieldLoadCancellation, cancellation)) _fieldLoadCancellation = null;
            cancellation.Dispose();
            return;
        }
        IsFieldLoading = true;
        EmptyStateText = $"Loading {field.Asset.FieldId}…";
        Status = $"Reading map geometry and {field.Asset.EventPaths.Count} event file(s)…";
        bool gateHeld = false;
        try
        {
            // Treat a superseded map load as a normal control-flow exit. Using the
            // token with WaitAsync/Task.Run caused Visual Studio to report the
            // expected cancellation as a user-unhandled exception while switching
            // fields quickly.
            await _fieldLoadGate.WaitAsync();
            gateHeld = true;
            if (token.IsCancellationRequested) return;
            if (Interlocked.Exchange(ref _memoryReleasePending, 0) != 0)
            {
                Status = "Releasing the previous map from memory…";
                await Task.Run(CompactReleasedMapMemory);
                if (token.IsCancellationRequested) return;
                Status = $"Reading map geometry and {field.Asset.EventPaths.Count} event file(s)…";
            }
            TreasureCatalog catalog = _index.Catalog;
            FieldLoadResult? loaded = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return null;
                TreasureMapIndex fieldIndex = TreasureMapIndexBuilder.BuildField(catalog, field.Asset);
                if (token.IsCancellationRequested) return null;
                Map1Archive archive = Map1Archive.Read(field.Asset.MapPath);
                if (token.IsCancellationRequested) return null;
                GuideMapGeometry geometry = GuideMapGeometry.Read(archive);
                if (token.IsCancellationRequested) return null;
                ChestLocationIndex locations = ChestLocationIndexBuilder.Build(fieldIndex);
                if (token.IsCancellationRequested) return null;
                return new FieldLoadResult(fieldIndex, geometry, locations, fieldIndex.Failures);
            });
            if (loaded is null || token.IsCancellationRequested) return;
            if (generation != _fieldLoadGeneration || !ReferenceEquals(field, SelectedField)) return;
            _currentGeometry = loaded.Geometry;
            _currentFieldIndex = loaded.Index;
            _locations = loaded.Locations;
            field.ChestCount = loaded.Locations.WorkerCount;
            NotifyPlacementChanged();
            LoadNpcRewards();
            LoadChests();
            EmptyStateText = loaded.Geometry.Models.Count == 0
                ? "This field does not contain dedicated guide-map geometry."
                    : loaded.Locations.WorkerCount == 0
                    ? "No confirmed chest workers were found in this field."
                    : MapChests.Count == 0
                        ? "No positioned chest icons match this map state. Use Previous Chest or Next Chest to inspect detected chests."
                        : "";
            Status = loaded.Failures.Count == 0
                ? $"{field.Asset.FieldId}: {loaded.Locations.WorkerCount} chest worker(s), {loaded.Geometry.Models.Count} map state(s)."
                : $"{field.Asset.FieldId}: loaded with {loaded.Failures.Count} parse warning(s).";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer selection owns the loading indicator and status text.
        }
        catch (Exception ex)
        {
            if (generation != _fieldLoadGeneration) return;
            EmptyStateText = $"This field could not be loaded: {ex.Message}";
            Status = $"{field.Asset.FieldId} failed to load.";
        }
        finally
        {
            if (gateHeld) _fieldLoadGate.Release();
            if (generation == _fieldLoadGeneration) { IsFieldLoading = false; NotifyMapChanged(); }
            if (ReferenceEquals(_fieldLoadCancellation, cancellation)) _fieldLoadCancellation = null;
            cancellation.Dispose();
        }
    }

    private static void CompactReleasedMapMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private void NotifyMapChanged()
    {
        OnPropertyChanged(nameof(CurrentModel));
        OnPropertyChanged(nameof(HasCurrentModel));
        OnPropertyChanged(nameof(ModelCount));
        OnPropertyChanged(nameof(ModelText));
        NotifyMapStateNavigation();
    }

    private void NotifyMapStateNavigation()
    {
        OnPropertyChanged(nameof(HasMultipleMapStates));
        OnPropertyChanged(nameof(CanPreviousMapState));
        OnPropertyChanged(nameof(CanNextMapState));
        OnPropertyChanged(nameof(MapStateNavigationText));
    }

    private void NotifyPlacementChanged()
    {
        OnPropertyChanged(nameof(DetectedChestCount));
        OnPropertyChanged(nameof(PlacedChestCount));
        OnPropertyChanged(nameof(UnplacedChestCount));
        OnPropertyChanged(nameof(HasPlacementNotice));
        OnPropertyChanged(nameof(PlacementNotice));
    }

    private void ClearChests()
    {
        foreach (TreasureChestRow row in _modelChests) row.Edited -= RowEdited;
        _modelChests.Clear();
        Chests.Clear();
        MapChests.Clear();
        SelectedChest = null;
        RefreshChestNavigation();
    }

    private void LoadChests()
    {
        _loading = true;
        ClearChests();
        if (SelectedField is not null && _locations is not null)
        {
            foreach (IGrouping<(string EventId, int WorkerIndex), ProjectedChestLocation> group in
                _locations.Locations
                    .Where(location => location.FieldId == SelectedField.Asset.FieldId)
                    .GroupBy(location => (location.EventId, location.WorkerIndex)))
            {
                ProjectedChestLocation location = group.FirstOrDefault(item => item.ModelIndex == SelectedModelIndex)
                    ?? group.FirstOrDefault(item => item.ModelIndex.HasValue)
                    ?? group.First();
                TreasureRecord? record = location.TreasureIds.Count == 1 && location.TreasureIds[0] < _index.Catalog.Records.Count
                    ? (_edits.TryGetValue(location.TreasureIds[0], out TreasureRecord? edited) ? edited : _index.Catalog.Records[location.TreasureIds[0]]) : null;
                var row = new TreasureChestRow(location, record, Project_Service.Instance.ProjectPath!);
                row.SetAvailableRewards(location.TreasureIds
                    .Where(id => id >= 0 && id < _index.Catalog.Records.Count)
                    .Select(id => _edits.TryGetValue(id, out TreasureRecord? editedReward)
                        ? editedReward : _index.Catalog.Records[id]));
                row.Edited += RowEdited;
                _modelChests.Add(row);
                if (location.ModelIndex == SelectedModelIndex) MapChests.Add(row);
            }
        }
        foreach (TreasureChestRow row in _modelChests) Chests.Add(row);
        SelectedChest = Chests.FirstOrDefault();
        RefreshChestNavigation();
        _loading = false;
    }

    private void LoadNpcRewards()
    {
        ClearNpcRewards();
        if (_currentFieldIndex is null) return;

        int[] treasureIds = _currentFieldIndex.Candidates
            .Where(candidate => !candidate.HasChestModel)
            .SelectMany(candidate => candidate.TreasureIds)
            .Where(id => id >= 0 && id < _index.Catalog.Records.Count)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        foreach (int treasureId in treasureIds)
        {
            TreasureRecord record = _edits.TryGetValue(treasureId, out TreasureRecord? edited)
                ? edited
                : _index.Catalog.Records[treasureId];
            var row = new NpcTreasureRow(treasureId, record, Project_Service.Instance.ProjectPath!);
            row.Edited += NpcRewardEdited;
            NpcRewards.Add(row);
        }
        SelectedNpcReward = NpcRewards.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoNpcRewards));
    }

    private void ClearNpcRewards()
    {
        foreach (NpcTreasureRow row in NpcRewards) row.Edited -= NpcRewardEdited;
        NpcRewards.Clear();
        SelectedNpcReward = null;
        OnPropertyChanged(nameof(HasNoNpcRewards));
    }

    private void NpcRewardEdited(object? sender, EventArgs e)
    {
        if (_loading || sender is not NpcTreasureRow row) return;
        TreasureRecord current = _edits.TryGetValue(row.TreasureId, out TreasureRecord? edited)
            ? edited
            : _index.Catalog.Records[row.TreasureId];
        if (RewardValuesMatch(current, row)) return;
        RecordHistory();
        TreasureRecord source = _index.Catalog.Records[row.TreasureId];
        _edits[row.TreasureId] = source with
        {
            RawKind = row.RawKind,
            Quantity = row.Quantity,
            Type = row.Type
        };
        IsDirty = true;
        NotifyHistoryState();
    }

    private void RowEdited(object? sender, EventArgs e)
    {
        if (_loading || sender is not TreasureChestRow row || row.TreasureId < 0) return;
        if (row.ActiveReward is not NpcTreasureRow reward) return;
        TreasureRecord current = _edits.TryGetValue(reward.TreasureId, out TreasureRecord? edited)
            ? edited
            : _index.Catalog.Records[reward.TreasureId];
        if (RewardValuesMatch(current, reward)) return;
        RecordHistory();
        TreasureRecord source = _index.Catalog.Records[reward.TreasureId];
        _edits[reward.TreasureId] = source with
        {
            RawKind = reward.RawKind,
            Quantity = reward.Quantity,
            Type = reward.Type
        };
        IsDirty = true;
        NotifyHistoryState();
    }
    public void NextModel(int delta)
    {
        if (ModelCount == 0) return;
        int next = SelectedModelIndex + delta;
        if (next < 0 || next >= ModelCount) return;
        SelectedModelIndex = next;
    }

    public void NextChest(int delta)
    {
        if (Chests.Count == 0) return;
        int index = SelectedChest is null ? 0 : Chests.IndexOf(SelectedChest);
        if (index < 0) index = 0;
        int next = index + delta;
        if (next < 0 || next >= Chests.Count) return;
        SelectedChest = Chests[next];
    }

    public void Save()
    {
        var edits = _index.Catalog.Records.ToDictionary(record => record.Id);
        foreach ((int id, TreasureRecord record) in _edits) edits[id] = record;
        byte[] output = TreasureCatalogWriter.Write(_index.Catalog, edits.Values);
        TreasureCatalog saved = TreasureCatalogSaveTransaction.Save(_index.Catalog, output);
        _index = _index with { Catalog = saved };
        _edits.Clear();
        _undoHistory.Clear();
        _redoHistory.Clear();
        IsDirty = false;
        HistoryStatus = "";
        NotifyHistoryState();
        Status = EditorSaveStatus.Success("Treasure catalog");
    }

    public void SaveToMaster(string masterPath)
    {
        var edits = _index.Catalog.Records.ToDictionary(record => record.Id);
        foreach ((int id, TreasureRecord record) in _edits) edits[id] = record;
        byte[] output = TreasureCatalogWriter.Write(_index.Catalog, edits.Values);
        string relative = System.IO.Path.GetRelativePath(Project_Service.Instance.ProjectPath!, _index.Catalog.Path);
        TreasureCatalog target = TreasureCatalog.Read(System.IO.Path.Combine(masterPath, relative));
        _ = TreasureCatalogSaveTransaction.Save(target, output);
    }

    public string GetOriginalCatalogPath() =>
        VanillaReference_Service.ResolveProjectFile(CatalogPath) ??
        throw new InvalidOperationException(
            "The configured Original Game Files do not contain a matching takara.bin. " +
            "Use Recovery > Select Original Game Files to configure a clean master folder.");

    public void RestoreOriginalRewardAndSave(NpcTreasureRow reward, string originalCatalogPath)
    {
        ArgumentNullException.ThrowIfNull(reward);
        TreasureCatalog original = TreasureCatalog.Read(originalCatalogPath);
        if (reward.TreasureId < 0 || reward.TreasureId >= original.Records.Count)
            throw new InvalidOperationException($"Treasure #{reward.TreasureId} does not exist in the original catalog.");
        TreasureRecord restored = original.Records[reward.TreasureId];
        RecordHistory();
        _edits[reward.TreasureId] = restored;
        reward.ApplyRecord(restored);
        IsDirty = true;
        NotifyHistoryState();
        int treasureId = reward.TreasureId;
        Save();
        Status = $"Restored and saved original Treasure #{treasureId}.";
    }

    public void DiscardUnsavedChanges()
    {
        _edits.Clear();
        _undoHistory.Clear();
        _redoHistory.Clear();
        IsDirty = false;
        HistoryStatus = "";
        RefreshEditedRows();
        NotifyHistoryState();
        Status = "Unsaved treasure changes discarded.";
    }

    public void Undo()
    {
        if (_undoHistory.Count == 0) return;
        Dictionary<int, TreasureRecord> current = CloneEdits();
        Dictionary<int, TreasureRecord> target = _undoHistory.Pop();
        _redoHistory.Push(current);
        string detail = DescribeEditDifference(target, current);
        RestoreEdits(target);
        HistoryStatus = $"Undid: {detail}.";
    }

    public void Redo()
    {
        if (_redoHistory.Count == 0) return;
        Dictionary<int, TreasureRecord> current = CloneEdits();
        Dictionary<int, TreasureRecord> target = _redoHistory.Pop();
        _undoHistory.Push(current);
        string detail = DescribeEditDifference(current, target);
        RestoreEdits(target);
        HistoryStatus = $"Redid: {detail}.";
    }

    public void UndoAll()
    {
        if (!IsDirty) return;
        int count = _undoHistory.Count;
        while (_undoHistory.Count > 0)
            Undo();
        NotifyHistoryState();
        HistoryStatus = $"Undid all: {count} Treasure Map change{(count == 1 ? "" : "s")} since the last save.";
    }

    private static string DescribeEditDifference(
        IReadOnlyDictionary<int, TreasureRecord> before,
        IReadOnlyDictionary<int, TreasureRecord> after)
    {
        int[] ids = before.Keys.Union(after.Keys)
            .Where(id => !before.TryGetValue(id, out TreasureRecord? a) ||
                         !after.TryGetValue(id, out TreasureRecord? b) || a != b)
            .OrderBy(id => id).ToArray();
        return ids.Length switch
        {
            0 => "Treasure Map state",
            1 => $"Treasure #{ids[0]}",
            _ => $"{ids.Length} treasures (#{ids[0]}–#{ids[^1]})"
        };
    }

    private void RecordHistory()
    {
        _undoHistory.Push(CloneEdits());
        _redoHistory.Clear();
    }

    private Dictionary<int, TreasureRecord> CloneEdits() =>
        _edits.ToDictionary(pair => pair.Key, pair => pair.Value);

    private void RestoreEdits(Dictionary<int, TreasureRecord> snapshot)
    {
        _edits.Clear();
        foreach ((int id, TreasureRecord record) in snapshot)
            _edits[id] = record;
        IsDirty = _edits.Count > 0;
        RefreshEditedRows();
        NotifyHistoryState();
    }

    private static bool RewardValuesMatch(TreasureRecord record, NpcTreasureRow reward) =>
        record.RawKind == reward.RawKind &&
        record.Quantity == reward.Quantity &&
        record.Type == reward.Type;

    private void RefreshEditedRows()
    {
        if (_currentFieldIndex is null) return;
        LoadNpcRewards();
        LoadChests();
    }

    private void NotifyHistoryState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoAll));
    }

    private sealed record FieldLoadResult(
        TreasureMapIndex Index,
        GuideMapGeometry Geometry,
        ChestLocationIndex Locations,
        IReadOnlyList<TreasureScanFailure> Failures);

}
