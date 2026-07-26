using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Atel;

public static class AtelByteRange
{
    public static (int Offset, int Length)? FindChangedRange(IReadOnlyList<byte> before,
        IReadOnlyList<byte> after)
    {
        int prefix = 0;
        int commonLength = Math.Min(before.Count, after.Count);
        while (prefix < commonLength && before[prefix] == after[prefix])
            prefix++;

        if (prefix == before.Count && prefix == after.Count) return null;

        int suffix = 0;
        while (suffix < commonLength - prefix &&
            before[before.Count - 1 - suffix] == after[after.Count - 1 - suffix])
            suffix++;

        int changedLength = after.Count - prefix - suffix;
        if (changedLength > 0)
            return (prefix, changedLength);

        // A deletion has no restored bytes. Select the nearest surviving byte.
        if (after.Count > 0)
            return (Math.Min(prefix, after.Count - 1), 1);

        return null;
    }
}
