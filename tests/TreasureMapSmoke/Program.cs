using FFXProjectEditor.FfxLib.TreasureMap;

if (args.Length is < 1 or > 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: TreasureMapSmoke <ffx/master folder> [preview-svg]");
    return 2;
}

string master = Path.GetFullPath(args[0]);
TreasureMapPrerequisiteResult completePrerequisites = TreasureMapPrerequisites.Validate(master);
if (!completePrerequisites.IsValid)
    throw new InvalidDataException(completePrerequisites.Message);
if (args.Length == 2 && args[1].StartsWith("probe=", StringComparison.OrdinalIgnoreCase))
{
    string fieldId = args[1]["probe=".Length..];
    TreasureCatalog probeCatalog = TreasureCatalog.Read(Path.Combine(master, "jppc", "battle", "kernel", "takara.bin"));
    FieldMapAsset probeField = FieldAssetDiscovery.ScanMaster(master).First(field => field.FieldId == fieldId);
    TreasureMapIndex probeIndex = TreasureMapIndexBuilder.BuildField(probeCatalog, probeField);
    ChestLocationIndex probeLocations = ChestLocationIndexBuilder.Build(probeIndex);
    foreach (EventTreasureCandidate candidate in probeIndex.ConfirmedChestCandidates)
        Console.WriteLine($"CANDIDATE {candidate.EventId} w{candidate.WorkerIndex:X2} treasure={string.Join(',', candidate.TreasureIds)} " +
            $"positions={string.Join(';', candidate.InitialPositions.Select(position => $"({position.X},{position.Y},{position.Z}) f{position.FunctionIndex}"))}");
    foreach (ProjectedChestLocation location in probeLocations.Locations)
        Console.WriteLine($"LOCATION w{location.WorkerIndex:X2} treasure={string.Join(',', location.TreasureIds)} model={location.ModelIndex?.ToString() ?? "none"} evidence={location.Evidence}");
    return 0;
}
string missingTestDirectory = Path.Combine(Path.GetTempPath(), "ZanarkandWorkshop-TreasureMissing-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(missingTestDirectory);
try
{
    TreasureMapPrerequisiteResult missing = TreasureMapPrerequisites.Validate(missingTestDirectory);
    if (missing.IsValid || missing.MissingPaths.Count != 4)
        throw new InvalidDataException("Partial-project validation did not report all four Treasure Map prerequisites.");
}
finally { try { Directory.Delete(missingTestDirectory, true); } catch { } }
TreasureMapIndex index = TreasureMapIndexBuilder.Build(master);
TreasureCatalog catalog = index.Catalog;
if (TreasureRewardLookup.Build(TreasureKind.Item, master).Count != 112)
    throw new InvalidDataException("Expected 112 friendly item reward options.");
if (TreasureRewardLookup.Build(TreasureKind.Equipment, master).Count != 86)
    throw new InvalidDataException("Expected 86 buki_get equipment reward options.");
foreach (TreasureRecord record in catalog.Records.Where(record => record.Kind.HasValue))
{
    string description = TreasureRewardLookup.Describe(record.Kind!.Value, record.Quantity, record.Type, master);
    if (description.StartsWith("Unknown / modded", StringComparison.Ordinal) && record.Kind != TreasureKind.KeyItem)
        throw new InvalidDataException($"Retail treasure {record.Id} did not translate: {description}");
}
if (catalog.Records.Count != 498)
    throw new InvalidDataException($"Expected 498 treasure records, found {catalog.Records.Count}.");
if (catalog.Records.Any(record => record.FileOffset != TreasureCatalog.HeaderLength + record.Id * 4))
    throw new InvalidDataException("Treasure record offsets are inconsistent.");

string saveTestDirectory = Path.Combine(Path.GetTempPath(), "ZanarkandWorkshop-TreasureSave-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(saveTestDirectory);
try
{
    string saveTestPath = Path.Combine(saveTestDirectory, "takara.bin");
    File.Copy(catalog.Path, saveTestPath);
    TreasureCatalog saveSource = TreasureCatalog.Read(saveTestPath);
    TreasureRecord[] supportedRewards =
    [
        saveSource.Records[0] with { RawKind = (byte)TreasureKind.Gil, Quantity = 123, Type = 0 },
        saveSource.Records[1] with { RawKind = (byte)TreasureKind.Item, Quantity = 7, Type = 0x2000 },
        saveSource.Records[2] with { RawKind = (byte)TreasureKind.KeyItem, Quantity = 1, Type = 0xA000 },
        saveSource.Records[3] with { RawKind = (byte)TreasureKind.Equipment, Quantity = 1, Type = 0 }
    ];
    TreasureRecord[] records = saveSource.Records
        .Select(record => record.Id < supportedRewards.Length ? supportedRewards[record.Id] : record)
        .ToArray();
    byte[] output = TreasureCatalogWriter.Write(saveSource, records);
    byte[] originalBytes = File.ReadAllBytes(saveSource.Path);
    if (!output.AsSpan(0, TreasureCatalog.HeaderLength).SequenceEqual(originalBytes.AsSpan(0, TreasureCatalog.HeaderLength)))
        throw new InvalidDataException("Treasure writer changed the catalog header.");
    if (originalBytes.Zip(output).Count(pair => pair.First != pair.Second) > supportedRewards.Length * TreasureCatalog.RecordLength)
        throw new InvalidDataException("Treasure writer changed bytes outside the edited records.");
    TreasureCatalog saved = TreasureCatalogSaveTransaction.Save(saveSource, output);
    if (!saved.Records.Take(supportedRewards.Length).SequenceEqual(supportedRewards) ||
        saved.Records.Skip(supportedRewards.Length).Where((record, index) => record != saveSource.Records[index + supportedRewards.Length]).Any())
        throw new InvalidDataException("Treasure save round-trip changed unexpected records.");

    byte[] wrongLength = output[..^1];
    try
    {
        TreasureCatalogSaveTransaction.Save(saved, wrongLength);
        throw new InvalidDataException("A wrong-length treasure catalog was accepted.");
    }
    catch (InvalidDataException ex) when (ex.Message.Contains("file length", StringComparison.Ordinal)) { }

    byte[] changedHeader = (byte[])output.Clone();
    changedHeader[0] ^= 1;
    try
    {
        TreasureCatalogSaveTransaction.Save(saved, changedHeader);
        throw new InvalidDataException("A changed treasure header was accepted.");
    }
    catch (InvalidDataException ex) when (ex.Message.Contains("header", StringComparison.Ordinal)) { }
}
finally
{
    try { Directory.Delete(saveTestDirectory, true); } catch { }
}

IReadOnlyList<FieldMapAsset> fields = index.Fields;
if (fields.Count < 400)
    throw new InvalidDataException($"Expected at least 400 MAP1 fields, found {fields.Count}.");

FieldMapAsset lazyTestField = fields.First(field => field.FieldId == "kami00");
TreasureMapIndex lazyFieldIndex = TreasureMapIndexBuilder.BuildField(catalog, lazyTestField);
ChestLocationIndex lazyLocations = ChestLocationIndexBuilder.Build(lazyFieldIndex);
if (lazyFieldIndex.Failures.Count != 0 || lazyLocations.WorkerCount != 5)
    throw new InvalidDataException($"Lazy kami00 scan expected five chest workers, found {lazyLocations.WorkerCount}.");

FieldMapAsset appendedInitializerField = fields.First(field => field.FieldId == "klyt00");
TreasureMapIndex appendedInitializerIndex = TreasureMapIndexBuilder.BuildField(catalog, appendedInitializerField);
ChestLocationIndex appendedInitializerLocations = ChestLocationIndexBuilder.Build(appendedInitializerIndex);
if (appendedInitializerLocations.WorkerCount != 3 ||
    appendedInitializerLocations.Locations.Any(location => location.ModelIndex is null))
    throw new InvalidDataException("Expected all three klyt00 chest initializers to project onto its guide map.");

EventTreasureCandidate[] candidates = index.Candidates.ToArray();
ChestLocationIndex locations = ChestLocationIndexBuilder.Build(index);
int guideMapFields = 0;
int guideMapModels = 0;
var guideMapFailures = new List<string>();
foreach (FieldMapAsset field in fields)
{
    try
    {
        GuideMapGeometry guide = GuideMapGeometry.Read(Map1Archive.Read(field.MapPath));
        if (guide.Models.Count > 0) guideMapFields++;
        guideMapModels += guide.Models.Count;
    }
    catch (Exception ex)
    {
        guideMapFailures.Add($"{field.FieldId}: {ex.Message}");
    }
}
int mappedFields = fields.Count(field => field.HasEvents);
int directCandidates = candidates.Count(candidate => candidate.IsDirectlyMappable);
int chestModelCandidates = candidates.Count(candidate => candidate.HasChestModel);
int exactChestLocations = candidates.Count(candidate => candidate.LocationConfidence == ChestLocationConfidence.Exact);
int conditionalChestLocations = candidates.Count(candidate => candidate.LocationConfidence == ChestLocationConfidence.Conditional);
int unresolvedChestLocations = candidates.Count(candidate => candidate.LocationConfidence == ChestLocationConfidence.Unresolved);
int singleTreasureNoPosition = candidates.Count(candidate => candidate.HasSingleTreasure && candidate.Positions.Count == 0);
int multipleTreasures = candidates.Count(candidate => !candidate.HasSingleTreasure);
int multiplePositions = candidates.Count(candidate => candidate.Positions.Count > 1);
int treasureReferences = candidates.Sum(candidate => candidate.TreasureIds.Count);


Console.WriteLine($"Treasure records: {catalog.Records.Count}");
Console.WriteLine($"MAP1 fields: {fields.Count}; fields matched to event files: {mappedFields}");
Console.WriteLine($"Guide-map fields: {guideMapFields}; guide-map models: {guideMapModels}");
Console.WriteLine($"Guide-map parse failures: {guideMapFailures.Count}");
foreach (string failure in guideMapFailures.Take(10)) Console.WriteLine("  " + failure);
Console.WriteLine($"Event files parsed: {index.EventScans.Count}; failures: {index.Failures.Count}");
Console.WriteLine($"Treasure candidate workers: {candidates.Length}; direct worker mappings: {directCandidates}");
Console.WriteLine($"Workers confirmed with chest models: {chestModelCandidates}");
Console.WriteLine($"Chest locations: exact={exactChestLocations}; conditional={conditionalChestLocations}; unresolved={unresolvedChestLocations}");
Console.WriteLine($"Projected records: exact={locations.ExactCount}; conditional={locations.ConditionalCount}; unresolved={locations.UnresolvedCount}");
Console.WriteLine($"Projected workers: total={locations.WorkerCount}; exact={locations.ExactWorkerCount}; conditional={locations.ConditionalWorkerCount}; unresolved={locations.UnresolvedWorkerCount}");
Console.WriteLine($"Single treasure/no position: {singleTreasureNoPosition}; multiple treasures: {multipleTreasures}; multiple positions: {multiplePositions}");
Console.WriteLine($"Distinct treasure references: {candidates.SelectMany(candidate => candidate.TreasureIds).Distinct().Count()}; total references: {treasureReferences}");

foreach (EventTreasureCandidate candidate in candidates.Where(candidate => candidate.IsDirectlyMappable).Take(20))
{
    EventPosition position = candidate.InitialPositions[0];
    Console.WriteLine(
        $"DIRECT {candidate.EventId} w{candidate.WorkerIndex:X2} treasure={candidate.TreasureIds[0]} " +
        $"position=({position.X}, {position.Y}, {position.Z})");
}
if (index.Failures.Count > 0)
{
    Console.WriteLine("Representative failures:");
    foreach (TreasureScanFailure failure in index.Failures.Take(20))
        Console.WriteLine($"  {Path.GetFileName(failure.Path)}: {failure.Message}");
}

if (args.Length == 2)
{
    FieldMapAsset previewField = fields.First(field => field.FieldId == "kami00");
    GuideMapModel previewModel = GuideMapGeometry.Read(Map1Archive.Read(previewField.MapPath)).Models[0];
    ProjectedChestLocation[] previewChests = locations.Locations
        .Where(location => location.FieldId == "kami00" && location.ModelIndex == 0)
        .ToArray();
    string previewPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
    File.WriteAllText(previewPath, GuideMapSvgRenderer.Render(previewModel, previewChests));
    Console.WriteLine($"Preview: {previewPath}");
}

return index.Failures.Count == 0 ? 0 : 1;
