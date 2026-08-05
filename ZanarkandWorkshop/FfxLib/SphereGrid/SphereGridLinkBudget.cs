using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.SphereGrid;

public readonly record struct SphereGridLinkPointBudget(
    int StraightLinks,
    int CurvedLinks,
    int MinimumPoints,
    int MaximumPoints)
{
    public const int Capacity = 4096;

    public bool DefinitelyOverCapacity => MinimumPoints > Capacity;
    public bool CouldExceedCapacity => MaximumPoints > Capacity;

    public static SphereGridLinkPointBudget Calculate(
        IReadOnlyList<SphereGridLink> links)
    {
        int straight = 0;
        int curved = 0;
        foreach (SphereGridLink link in links)
        {
            if (link.IsCurved)
                curved++;
            else
                straight++;
        }

        // This matches the game routines at 0xA581F0 and 0xA57710:
        // straight links always generate 2 points; curved links generate 4-8.
        return new SphereGridLinkPointBudget(
            straight,
            curved,
            checked(straight * 2 + curved * 4),
            checked(straight * 2 + curved * 8));
    }
}
