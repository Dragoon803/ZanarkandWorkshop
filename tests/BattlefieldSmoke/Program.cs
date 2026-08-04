using FFXProjectEditor.FfxLib.Battlefield;
using FFXProjectEditor.FfxLib.BattleFormation;

string root = args.Length > 0
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Pass the extracted FFX master folder as the first argument.");

IReadOnlyList<BattlefieldAsset> assets = BattlefieldAssetCatalog.Discover(root);
if (assets.Count < 50)
    throw new InvalidDataException($"Expected at least 50 known battlefields, found {assets.Count}.");

int vertices = 0;
int triangles = 0;
int surfaces = 0;
foreach (BattlefieldAsset asset in assets)
{
    if (!BattlefieldHeightMap.TryRead(asset.MapPath, out BattlefieldHeightMap? decoded))
        continue;
    BattlefieldHeightMap map = decoded!;
    surfaces++;
    if (map.Vertices.Count == 0 || map.Triangles.Count == 0)
        throw new InvalidDataException($"{asset.Code} contains no battlefield surface.");
    vertices += map.Vertices.Count;
    triangles += map.Triangles.Count;
}

BattlefieldFormationIndex formationIndex = BattlefieldFormationIndex.ReadProject(root);
string formationRoot = Path.Combine(root, "jppc", "battle", "btl");
string[] retailFormationNames = Directory.EnumerateFiles(formationRoot, "*.bin", SearchOption.AllDirectories)
    .Select(Path.GetFileNameWithoutExtension)
    .ToArray()!;
string[] missingAssignments = retailFormationNames
    .Where(name => !formationIndex.TryResolve(name, out _))
    .ToArray();
int coveredRetailFiles = retailFormationNames.Length - missingAssignments.Length;
if (coveredRetailFiles < 800)
    throw new InvalidDataException(
        $"Battle-list coverage is unexpectedly low: {coveredRetailFiles} of {retailFormationNames.Length} files.");
BattlefieldAsset? besaid = formationIndex.ResolveAsset("bsil03_00.bin", assets);
if (besaid?.Code != "bsil03_a")
    throw new InvalidDataException("The Besaid underwater formation did not resolve to bsil03_a.");

var variantChecks = new Dictionary<string, string>
{
    ["mihn00_00"] = "mihn00_a",
    ["mihn00_20"] = "mihn00_b",
    ["bika03_00"] = "bika01_a",
    ["bika03_10"] = "bika03_b",
    ["bika03_20"] = "bika03_c",
    ["bvyt09_00"] = "bvyt09_a",
    ["bvyt09_20"] = "bvyt09_b",
    ["nagi05_00"] = "nagi05_a",
    ["nagi05_10"] = "nagi05_b",
    ["nagi05_20"] = "nagi05_c"
};
foreach ((string formation, string expected) in variantChecks)
{
    string? actual = formationIndex.ResolveAsset(formation, assets)?.Code;
    if (actual != expected)
        throw new InvalidDataException($"{formation} resolved to {actual ?? "nothing"}, expected {expected}.");
}

int unresolvedBattlefieldIds = formationIndex.Assignments
    .Select(assignment => assignment.BattlefieldId)
    .Where(id => id >= 0x401)
    .Distinct()
    .Count(id => assets.All(asset => asset.Id != id));
if (unresolvedBattlefieldIds != 0)
    throw new InvalidDataException($"The battle list references {unresolvedBattlefieldIds} unknown battlefield IDs.");

BattlefieldHeightMap besaidMap = BattlefieldHeightMap.Read(besaid.MapPath);
string besaidFormationPath = Path.Combine(root, "jppc", "battle", "btl", "bsil03_00", "bsil03_00.bin");
BattleFormationFile besaidFormation = BattleFormationParser.Read(besaidFormationPath);
float minX = besaidMap.Vertices.Min(vertex => vertex.X);
float maxX = besaidMap.Vertices.Max(vertex => vertex.X);
float minZ = besaidMap.Vertices.Min(vertex => vertex.Z);
float maxZ = besaidMap.Vertices.Max(vertex => vertex.Z);
foreach (FormationPosition position in besaidFormation.Positions)
{
    if (position.X < minX || position.X > maxX || position.Z < minZ || position.Z > maxZ)
        throw new InvalidDataException($"{position.Kind} {position.Index + 1} is outside the decoded Besaid battlefield.");
}

if (surfaces < 45)
    throw new InvalidDataException($"Expected at least 45 battlefield surfaces, found {surfaces}.");

Console.WriteLine($"Battlefield assets: {assets.Count}; surfaces: {surfaces}; vertices: {vertices}; triangles: {triangles}");
Console.WriteLine($"Formation assignments: {formationIndex.Assignments.Count}; exact variant checks: {variantChecks.Count}");
Console.WriteLine($"Physical formations covered by btl.bin: {coveredRetailFiles}; direct/scripted formations: {missingAssignments.Length}");
Console.WriteLine("Battlefield discovery, decoding, and formation-coordinate alignment passed.");
