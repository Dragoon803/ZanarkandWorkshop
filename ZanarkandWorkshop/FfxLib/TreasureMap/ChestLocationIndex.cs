using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record ProjectedChestLocation(
    string FieldId,
    string EventId,
    string EventPath,
    int WorkerIndex,
    IReadOnlyList<int> TreasureIds,
    int? ModelIndex,
    EventPosition? WorldPosition,
    float? GuideX,
    float? GuideZ,
    float? PixelX,
    float? PixelY,
    ChestLocationConfidence Confidence,
    string Evidence);

public sealed record ChestLocationIndex(
    IReadOnlyList<ProjectedChestLocation> Locations,
    int Width,
    int Height)
{
    public int ExactCount => Locations.Count(location => location.Confidence == ChestLocationConfidence.Exact);
    public int ConditionalCount => Locations.Count(location => location.Confidence == ChestLocationConfidence.Conditional);
    public int UnresolvedCount => Locations.Count(location => location.Confidence == ChestLocationConfidence.Unresolved);
    public int WorkerCount => WorkerGroups.Count();
    public int ExactWorkerCount => CountWorkers(ChestLocationConfidence.Exact);
    public int ConditionalWorkerCount => CountWorkers(ChestLocationConfidence.Conditional);
    public int UnresolvedWorkerCount => CountWorkers(ChestLocationConfidence.Unresolved);

    private IEnumerable<IGrouping<(string EventId, int WorkerIndex), ProjectedChestLocation>> WorkerGroups =>
        Locations.GroupBy(location => (location.EventId, location.WorkerIndex));

    private int CountWorkers(ChestLocationConfidence confidence) => WorkerGroups.Count(group =>
        group.Max(location => location.Confidence == ChestLocationConfidence.Exact ? 2 :
            location.Confidence == ChestLocationConfidence.Conditional ? 1 : 0) ==
        (confidence == ChestLocationConfidence.Exact ? 2 :
            confidence == ChestLocationConfidence.Conditional ? 1 : 0));
}

public static class ChestLocationIndexBuilder
{
    public const float FieldWorldUnitsPerGuideUnit = 10f;

    public static ChestLocationIndex Build(TreasureMapIndex treasureIndex, int width = 900, int height = 700)
    {
        var fields = treasureIndex.Fields.ToDictionary(field => field.FieldId, StringComparer.OrdinalIgnoreCase);
        var geometry = new Dictionary<string, GuideMapGeometry>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ProjectedChestLocation>();

        foreach (EventTreasureCandidate candidate in treasureIndex.ConfirmedChestCandidates)
        {
            if (!fields.TryGetValue(candidate.FieldId, out FieldMapAsset? field))
            {
                results.Add(Unresolved(candidate, "No matching MAP1 field."));
                continue;
            }
            if (!geometry.TryGetValue(field.FieldId, out GuideMapGeometry? guide))
            {
                guide = GuideMapGeometry.Read(Map1Archive.Read(field.MapPath));
                geometry.Add(field.FieldId, guide);
            }
            if (guide.Models.Count == 0)
            {
                results.Add(Unresolved(candidate, "The field has no dedicated guide-map model."));
                continue;
            }
            if (candidate.InitialPositions.Count == 0)
            {
                results.Add(Unresolved(candidate, "No constant initialization position was recovered."));
                continue;
            }

            bool added = false;
            foreach (EventPosition position in candidate.InitialPositions)
            {
                float guideX = position.X / FieldWorldUnitsPerGuideUnit;
                float guideZ = position.Z / FieldWorldUnitsPerGuideUnit;
                int[] matchingModels = guide.Models.Select((model, index) => (model, index))
                    .Where(pair => Contains(pair.model, guideX, guideZ))
                    .Select(pair => pair.index)
                    .ToArray();
                foreach (int modelIndex in matchingModels)
                {
                    GuideMapProjection projection = GuideMapProjection.Fit(guide.Models[modelIndex], width, height);
                    (float pixelX, float pixelY) = projection.Project(guideX, guideZ);
                    ChestLocationConfidence confidence = candidate.HasSingleTreasure &&
                        candidate.InitialPositions.Count == 1 && matchingModels.Length == 1
                        ? ChestLocationConfidence.Exact
                        : candidate.HasSingleTreasure
                            ? ChestLocationConfidence.Conditional
                            : ChestLocationConfidence.Unresolved;
                    results.Add(new ProjectedChestLocation(
                        candidate.FieldId, candidate.EventId, candidate.EventPath, candidate.WorkerIndex,
                        candidate.TreasureIds, modelIndex, position, guideX, guideZ, pixelX, pixelY, confidence,
                        $"ATEL w{candidate.WorkerIndex:X2} init @ 0x{position.ScriptOffset:X}; " +
                        $"world/10; MAP1 section 11 YNGM state {modelIndex}"));
                    added = true;
                }
            }
            if (!added)
                results.Add(Unresolved(candidate, "Recovered positions fall outside every guide-map state."));
        }

        return new ChestLocationIndex(results, width, height);
    }

    private static bool Contains(GuideMapModel model, float x, float z)
    {
        float marginX = Math.Max(1f, (model.BoundsMax.X - model.BoundsMin.X) * 0.03f);
        float marginZ = Math.Max(1f, (model.BoundsMax.Z - model.BoundsMin.Z) * 0.03f);
        return x >= model.BoundsMin.X - marginX && x <= model.BoundsMax.X + marginX &&
               z >= model.BoundsMin.Z - marginZ && z <= model.BoundsMax.Z + marginZ;
    }

    private static ProjectedChestLocation Unresolved(EventTreasureCandidate candidate, string evidence) =>
        new(candidate.FieldId, candidate.EventId, candidate.EventPath, candidate.WorkerIndex,
            candidate.TreasureIds, null, null, null, null, null, null,
            ChestLocationConfidence.Unresolved, evidence);
}
