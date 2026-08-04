using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public enum SphereGridCharacter
{
    Unassigned,
    Tidus,
    Yuna,
    Auron,
    Kimahri,
    Wakka,
    Lulu,
    Rikku
}

public sealed record SphereGridCharacterStyle(
    SphereGridCharacter Character,
    string Name,
    string Color);

public sealed class SphereGridRouteMetadata
{
    private static readonly IReadOnlyDictionary<SphereGridCharacter, SphereGridCharacterStyle>
        Styles = new Dictionary<SphereGridCharacter, SphereGridCharacterStyle>
        {
            [SphereGridCharacter.Unassigned] = new(SphereGridCharacter.Unassigned, "Unassigned", "#77808C"),
            [SphereGridCharacter.Tidus] = new(SphereGridCharacter.Tidus, "Tidus", "#4DDDF8"),
            [SphereGridCharacter.Yuna] = new(SphereGridCharacter.Yuna, "Yuna", "#F4F1EA"),
            [SphereGridCharacter.Auron] = new(SphereGridCharacter.Auron, "Auron", "#E53935"),
            [SphereGridCharacter.Kimahri] = new(SphereGridCharacter.Kimahri, "Kimahri", "#3157D5"),
            [SphereGridCharacter.Wakka] = new(SphereGridCharacter.Wakka, "Wakka", "#F97316"),
            [SphereGridCharacter.Lulu] = new(SphereGridCharacter.Lulu, "Lulu", "#C44ED6"),
            [SphereGridCharacter.Rikku] = new(SphereGridCharacter.Rikku, "Rikku", "#32C84A")
        };

    private readonly SphereGridCharacter[] _characterByNode;

    public SphereGridKind Kind { get; }
    public IReadOnlyDictionary<SphereGridCharacter, SphereGridCharacterStyle> Palette => Styles;

    private SphereGridRouteMetadata(
        SphereGridKind kind,
        SphereGridCharacter[] characterByNode)
    {
        Kind = kind;
        _characterByNode = characterByNode;
    }

    public SphereGridCharacter GetCharacter(int nodeIndex) =>
        (uint)nodeIndex < (uint)_characterByNode.Length
            ? _characterByNode[nodeIndex]
            : SphereGridCharacter.Unassigned;

    public SphereGridCharacterStyle GetStyle(int nodeIndex) =>
        Styles[GetCharacter(nodeIndex)];

    public static SphereGridRouteMetadata Build(SphereGridGraph graph)
    {
        int nodeCount = graph.File.Nodes.Count;
        if (TryBuildCuratedStandard(graph.File, out SphereGridCharacter[] curated))
        {
            SmoothCuratedBoundaries(graph.File, curated);
            ApplyCuratedDefaults(graph.File, curated);
            return new SphereGridRouteMetadata(graph.File.Kind, curated);
        }
        if (TryBuildCuratedOriginal(graph.File, out curated))
        {
            ApplyCuratedDefaults(graph.File, curated);
            return new SphereGridRouteMetadata(graph.File.Kind, curated);
        }

        var distances = new Dictionary<SphereGridCharacter, int[]>();
        foreach (SphereGridCharacter character in PlayableCharacters)
        {
            int[] values = Enumerable.Repeat(int.MaxValue, nodeCount).ToArray();
            var queue = new Queue<int>();
            foreach (SphereGridNode node in graph.File.Nodes)
            {
                if (node.IsVisible && IsRouteSeed(character, node.Type))
                {
                    values[node.Index] = 0;
                    queue.Enqueue(node.Index);
                }
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int linkIndex in graph.GetLinkIndices(current))
                {
                    SphereGridLink link = graph.File.Links[linkIndex];
                    int neighbour = link.NodeAIndex == current
                        ? link.NodeBIndex
                        : link.NodeAIndex;
                    if (!graph.File.Nodes[neighbour].IsVisible ||
                        IsSectionBoundary(graph.File.Nodes[neighbour]) ||
                        values[neighbour] <= values[current] + 1)
                        continue;
                    values[neighbour] = values[current] + 1;
                    queue.Enqueue(neighbour);
                }
            }
            distances[character] = values;
        }

        var assignments = new SphereGridCharacter[nodeCount];
        foreach (SphereGridNode node in graph.File.Nodes)
        {
            if (!node.IsVisible)
                continue;
            int bestDistance = int.MaxValue;
            SphereGridCharacter best = SphereGridCharacter.Unassigned;
            foreach (SphereGridCharacter character in PlayableCharacters)
            {
                int distance = distances[character][node.Index];
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = character;
                }
            }
            assignments[node.Index] = bestDistance == int.MaxValue
                ? SphereGridCharacter.Unassigned
                : best;
        }

        // Lock nodes form the gates between the colored routes in the in-game
        // maps. They are deliberately excluded from propagation above, then
        // inherit a route only when every assigned side of the gate agrees.
        foreach (SphereGridNode node in graph.File.Nodes)
        {
            if (!node.IsVisible || !IsSectionBoundary(node))
                continue;
            SphereGridCharacter[] neighbours = graph.GetLinkIndices(node.Index)
                .Select(linkIndex =>
                {
                    SphereGridLink link = graph.File.Links[linkIndex];
                    int neighbour = link.NodeAIndex == node.Index
                        ? link.NodeBIndex
                        : link.NodeAIndex;
                    return assignments[neighbour];
                })
                .Where(character => character != SphereGridCharacter.Unassigned)
                .ToArray();
            assignments[node.Index] = neighbours
                .GroupBy(character => character)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => System.Array.IndexOf(PlayableCharacters, group.Key))
                .Select(group => group.Key)
                .FirstOrDefault();
        }

        FillUnassignedIslands(graph, assignments);
        SimplifyTerritories(graph, assignments);
        ApplyCuratedDefaults(graph.File, assignments);
        return new SphereGridRouteMetadata(graph.File.Kind, assignments);
    }

    private static void ApplyCuratedDefaults(
        SphereGridFile file,
        SphereGridCharacter[] assignments)
    {
        if (file.Kind == SphereGridKind.Expert)
        {
            Assign(SphereGridCharacter.Tidus,
            [
                87, 79, 779, 126, 125, 78, 77, 127, 128, 129, 75, 131
            ]);
            Assign(SphereGridCharacter.Lulu,
            [
                660, 664, 662
            ]);
            Assign(SphereGridCharacter.Kimahri,
            [
                32, 37
            ]);
            Assign(SphereGridCharacter.Yuna,
            [
                5, 148, 160, 161
            ]);
            Assign(SphereGridCharacter.Wakka,
            [
                223, 227, 228, 181, 235, 532, 533, 230, 231, 234, 535,
                236, 237, 205, 206, 210, 226
            ]);
            return;
        }

        if (file.Kind is not (SphereGridKind.Standard or SphereGridKind.Original))
            return;

        Assign(SphereGridCharacter.Yuna,
        [
            345, 346, 273, 354, 355, 361, 356, 360, 357, 438, 446, 445,
            444, 443, 733, 715, 716, 708, 714, 717, 709
        ]);
        Assign(SphereGridCharacter.Tidus,
        [
            13, 136, 703, 137, 139, 784, 782, 783
        ]);
        Assign(SphereGridCharacter.Rikku,
        [
            842, 840, 844, 671, 522, 527, 528
        ]);
        Assign(SphereGridCharacter.Lulu,
        [
            321, 320, 297, 296
        ]);
        Assign(SphereGridCharacter.Kimahri,
        [
            640, 679, 678, 695, 696
        ]);
        Assign(SphereGridCharacter.Wakka,
        [
            184, 217
        ]);

        if (file.Kind == SphereGridKind.Standard)
        {
            Assign(SphereGridCharacter.Wakka,
            [
                650, 649
            ]);
            Assign(SphereGridCharacter.Auron,
            [
                593, 603, 837, 836, 835, 838, 839, 604, 605, 606, 820,
                821
            ]);
        }
        else if (file.Kind == SphereGridKind.Original)
        {
            Assign(SphereGridCharacter.Kimahri,
            [
                667
            ]);
        }

        void Assign(SphereGridCharacter character, int[] nodeIndices)
        {
            foreach (int nodeIndex in nodeIndices)
            {
                if ((uint)nodeIndex < (uint)assignments.Length &&
                    file.Nodes[nodeIndex].IsVisible)
                    assignments[nodeIndex] = character;
            }
        }
    }

    private static bool TryBuildCuratedStandard(
        SphereGridFile file,
        out SphereGridCharacter[] assignments)
    {
        var result = new SphereGridCharacter[file.Nodes.Count];
        assignments = result;
        if (file.Kind != SphereGridKind.Standard || file.Nodes.Count < 860)
            return false;

        Assign(SphereGridCharacter.Tidus,
            "0-9,11-12,14-15,17-55,93-106,109-135,138,140-145,156,213-214," +
            "377,382,391,678-680,695-696,702,708-709,714-717,733,781,785," +
            "812-815,828-829");
        Assign(SphereGridCharacter.Yuna,
            "107-108,136-137,139,146,272,335-344,347-353,358-359,362-376," +
            "378-381,383-390,392-402,404-405,408-437,439-442,447,699-701," +
            "703-707,710-713,718,754,804,811,817-818,822-825,845-850");
        Assign(SphereGridCharacter.Auron,
            "10,13,16,184,217,296,538-592,594-602,607-635,640,649-650," +
            "734-740,762-766,772-780,783-784,833-834");
        Assign(SphereGridCharacter.Kimahri,
            "273,316,345-346,354-357,360-361,636-639,641-648,652-664," +
            "666-671,677,681-694,840,842,844");
        Assign(SphereGridCharacter.Wakka,
            "56-65,67-92,147-155,157-183,185-212,215-216,218-219,297," +
            "299-300,320-321,334,533-534,537,593,603-606,651,741-744," +
            "767-768,805-810,819-821,826,830-832,835-839");
        Assign(SphereGridCharacter.Lulu,
            "66,220-271,274-295,298,301-315,317-319,322-333,496,522," +
            "527-532,535-536,697-698,745-750,769-771,782,786-803,851-859");
        Assign(SphereGridCharacter.Rikku,
            "403,406-407,438,443-446,448-495,497-521,523-526,665,672-676," +
            "719-732,751-753,755-761,816,827,841,843");
        AssignAppendedNodes(file, result, 860);
        return result.Take(860).All(character => character != SphereGridCharacter.Unassigned);

        void Assign(SphereGridCharacter character, string ranges)
        {
            foreach (string item in ranges.Split(','))
            {
                string[] limits = item.Split('-');
                int first = int.Parse(limits[0]);
                int last = limits.Length == 1 ? first : int.Parse(limits[1]);
                for (int index = first; index <= last; index++)
                    result[index] = character;
            }
        }
    }

    private static bool TryBuildCuratedOriginal(
        SphereGridFile file,
        out SphereGridCharacter[] assignments)
    {
        var result = new SphereGridCharacter[file.Nodes.Count];
        assignments = result;
        if (file.Kind != SphereGridKind.Original || file.Nodes.Count < 828)
            return false;

        Assign(SphereGridCharacter.Tidus,
            "0-9,11-55,93-145,156,213-214,377,680,702-703,781-785,812-815");
        Assign(SphereGridCharacter.Yuna,
            "146,272-273,335-376,378-405,408-447,699-701,704-718,733," +
            "754-755,804,811,817-818,822-825");
        Assign(SphereGridCharacter.Auron,
            "10,538-635,734-740,762-766,772-780,820-821");
        Assign(SphereGridCharacter.Kimahri,
            "636-648,652-664,666,668-671,677-679,681-696");
        Assign(SphereGridCharacter.Wakka,
            "56-92,147-155,157-212,215-219,299-300,334,533-534,537," +
            "649-651,741-744,767-768,805-810,819,826");
        Assign(SphereGridCharacter.Lulu,
            "220-271,274-298,301-333,496,529-532,535-536,697-698," +
            "745-750,769-771,786-803");
        Assign(SphereGridCharacter.Rikku,
            "406-407,448-495,497-528,665,667,672-676,719-732,751-753," +
            "756-761,816,827");
        AssignAppendedNodes(file, result, 828);
        return result.Take(828).All(character => character != SphereGridCharacter.Unassigned);

        void Assign(SphereGridCharacter character, string ranges)
        {
            foreach (string item in ranges.Split(','))
            {
                string[] limits = item.Split('-');
                int first = int.Parse(limits[0]);
                int last = limits.Length == 1 ? first : int.Parse(limits[1]);
                for (int index = first; index <= last; index++)
                    result[index] = character;
            }
        }
    }

    private static void AssignAppendedNodes(
        SphereGridFile file,
        SphereGridCharacter[] assignments,
        int originalNodeCount)
    {
        for (int nodeIndex = originalNodeCount; nodeIndex < assignments.Length; nodeIndex++)
        {
            foreach (SphereGridLink link in file.Links)
            {
                int neighbour = link.NodeAIndex == nodeIndex
                    ? link.NodeBIndex
                    : link.NodeBIndex == nodeIndex
                        ? link.NodeAIndex
                        : -1;
                if (neighbour >= 0 && neighbour < assignments.Length &&
                    assignments[neighbour] != SphereGridCharacter.Unassigned)
                {
                    assignments[nodeIndex] = assignments[neighbour];
                    break;
                }
            }
        }
    }

    private static void SmoothCuratedBoundaries(
        SphereGridFile file,
        SphereGridCharacter[] assignments)
    {
        const double neighbourRadiusSquared = 180 * 180;
        for (int pass = 0; pass < 2; pass++)
        {
            SphereGridCharacter[] source = (SphereGridCharacter[])assignments.Clone();
            foreach (SphereGridNode node in file.Nodes.Where(node => node.IsVisible))
            {
                SphereGridCharacter[] neighbours = file.Nodes
                    .Where(candidate => candidate.IsVisible &&
                                        candidate.Index != node.Index)
                    .Select(candidate => new
                    {
                        candidate.Index,
                        Distance =
                            (candidate.X - node.X) * (candidate.X - node.X) +
                            (candidate.Y - node.Y) * (candidate.Y - node.Y)
                    })
                    .Where(candidate => candidate.Distance <= neighbourRadiusSquared)
                    .OrderBy(candidate => candidate.Distance)
                    .Take(10)
                    .Select(candidate => source[candidate.Index])
                    .ToArray();
                if (neighbours.Length < 5)
                    continue;

                IGrouping<SphereGridCharacter, SphereGridCharacter> majority =
                    neighbours.GroupBy(character => character)
                        .OrderByDescending(group => group.Count())
                        .First();
                if (majority.Key != source[node.Index] &&
                    majority.Count() >= 4 &&
                    majority.Count() * 5 >= neighbours.Length * 4)
                    assignments[node.Index] = majority.Key;
            }
        }
    }

    private static void SimplifyTerritories(
        SphereGridGraph graph,
        SphereGridCharacter[] assignments)
    {
        const int maximumIslandSize = 12;
        for (int pass = 0; pass < 3; pass++)
        {
            var visited = new HashSet<int>();
            bool changed = false;
            foreach (SphereGridNode start in graph.VisibleNodes)
            {
                if (!visited.Add(start.Index))
                    continue;

                SphereGridCharacter character = assignments[start.Index];
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start.Index);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    foreach (int linkIndex in graph.GetLinkIndices(current))
                    {
                        SphereGridLink link = graph.File.Links[linkIndex];
                        int neighbour = link.NodeAIndex == current
                            ? link.NodeBIndex
                            : link.NodeAIndex;
                        if (graph.File.Nodes[neighbour].IsVisible &&
                            assignments[neighbour] == character &&
                            visited.Add(neighbour))
                            queue.Enqueue(neighbour);
                    }
                }

                if (component.Count > maximumIslandSize ||
                    character == SphereGridCharacter.Kimahri)
                    continue;

                SphereGridCharacter replacement = component
                    .SelectMany(graph.GetLinkIndices)
                    .Select(linkIndex => graph.File.Links[linkIndex])
                    .SelectMany(link => new[] { (int)link.NodeAIndex, link.NodeBIndex })
                    .Where(index => !component.Contains(index) &&
                                    graph.File.Nodes[index].IsVisible)
                    .Select(index => assignments[index])
                    .Where(candidate => candidate != character &&
                                        candidate != SphereGridCharacter.Unassigned)
                    .GroupBy(candidate => candidate)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => System.Array.IndexOf(PlayableCharacters, group.Key))
                    .Select(group => group.Key)
                    .FirstOrDefault();
                if (replacement == SphereGridCharacter.Unassigned)
                    continue;
                foreach (int nodeIndex in component)
                    assignments[nodeIndex] = replacement;
                changed = true;
            }
            if (!changed)
                break;
        }
    }

    private static bool IsSectionBoundary(SphereGridNode node) =>
        node.TypeInfo.Category == SphereGridNodeCategory.Lock;

    private static void FillUnassignedIslands(
        SphereGridGraph graph,
        SphereGridCharacter[] assignments)
    {
        int[] distance = Enumerable.Repeat(int.MaxValue, assignments.Length).ToArray();
        var queue = new Queue<int>();
        foreach (SphereGridNode node in graph.File.Nodes)
        {
            if (!node.IsVisible ||
                assignments[node.Index] == SphereGridCharacter.Unassigned)
                continue;
            distance[node.Index] = 0;
            queue.Enqueue(node.Index);
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int linkIndex in graph.GetLinkIndices(current))
            {
                SphereGridLink link = graph.File.Links[linkIndex];
                int neighbour = link.NodeAIndex == current
                    ? link.NodeBIndex
                    : link.NodeAIndex;
                if (!graph.File.Nodes[neighbour].IsVisible)
                    continue;
                int candidateDistance = distance[current] + 1;
                if (candidateDistance < distance[neighbour])
                {
                    distance[neighbour] = candidateDistance;
                    if (assignments[neighbour] == SphereGridCharacter.Unassigned)
                        assignments[neighbour] = assignments[current];
                    queue.Enqueue(neighbour);
                }
            }
        }
    }

    private static readonly SphereGridCharacter[] PlayableCharacters =
    {
        SphereGridCharacter.Tidus,
        SphereGridCharacter.Yuna,
        SphereGridCharacter.Auron,
        SphereGridCharacter.Kimahri,
        SphereGridCharacter.Wakka,
        SphereGridCharacter.Lulu,
        SphereGridCharacter.Rikku
    };

    private static bool IsRouteSeed(SphereGridCharacter character, byte type) =>
        character switch
        {
            SphereGridCharacter.Tidus =>
                type is 0x2A or 0x2B or 0x39 or 0x3C or 0x3E or
                    0x4F or 0x59 or 0x5A or 0x5B or 0x5C,
            SphereGridCharacter.Yuna =>
                type is 0x3D or
                    >= 0x4E and <= 0x53 or
                    >= 0x55 and <= 0x58 or
                    >= 0x5D and <= 0x63,
            SphereGridCharacter.Auron =>
                type is >= 0x34 and <= 0x37 or
                    0x45 or 0x46 or 0x48,
            SphereGridCharacter.Kimahri =>
                type is 0x44 or 0x54 or 0x76,
            SphereGridCharacter.Wakka =>
                type is >= 0x2C and <= 0x33 or 0x41 or 0x42 or 0x43,
            SphereGridCharacter.Lulu =>
                type is >= 0x64 and <= 0x75 or 0x4C,
            SphereGridCharacter.Rikku =>
                type is 0x38 or 0x3A or 0x3B or 0x40 or 0x47 or 0x49 or
                    0x4A or 0x4B or 0x4D or 0x77 or 0x78 or 0x7D or 0x7E,
            _ => false
        };
}
