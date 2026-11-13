namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The grouping of <see cref="HostAliasGroups.Find"/>: the extent, copy directions and device alignment of each
/// group of overlapping arguments, and the checks that a group can share one copy.
/// </summary>
internal static class HostAliasGrouping
{
    // The widest alignment that a device element needs (a 16-byte vector type)
    private const int MaxAlignment = 16;

    // The extent, the copy directions and the device alignment of group g, and the offset of each member in it
    internal static HostAliasGroup BuildGroup(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, string names,
        int[] groupOf, int g, nint[] offsets)
    {
        var leader = -1;
        nint start = 0;
        nint end = 0;
        var group = new HostAliasGroup { KernelGetsCopy = true, ViewsOnly = true, Alignment = 1 };
        for (var i = 0; i < groupOf.Length; i++)
        {
            if (groupOf[i] != g)
            {
                continue;
            }

            var address = ranges[i * HostAliasGroups.RangeFields];
            var length = ranges[(i * HostAliasGroups.RangeFields) + 1];
            if (leader < 0 || address < start)
            {
                leader = i;
                start = address;
            }

            end = Math.Max(end, address + length);

            group.CopiesIn |= (traits[i] & HostAliasGroups.CopiesIn) != 0;
            group.CopiesBack |= (traits[i] & HostAliasGroups.CopiesBack) != 0;
            group.ViewsOnly &= (traits[i] & HostAliasGroups.SpanView) != 0;
            group.KernelGetsCopy &= (traits[i] & (HostAliasGroups.CopiesIn | HostAliasGroups.CopiesBack)) ==
                HostAliasGroups.CopiesIn;
            group.Alignment = Math.Max(group.Alignment, GetAlignment(ranges[(i * HostAliasGroups.RangeFields) + 2]));
        }

        group.Names = JoinNames(names, groupOf, g);
        group.Leader = leader;
        group.Length = end - start;
        for (var i = 0; i < groupOf.Length; i++)
        {
            if (groupOf[i] == g)
            {
                offsets[i] = ranges[i * HostAliasGroups.RangeFields] - start;
            }
        }

        CheckSupported(traits, groupOf, g, group.Names);
        group.Shift = GetShift(ranges, groupOf, g, offsets, group.Alignment, group.Names);
        return group;
    }

    private static void CheckSupported(ReadOnlySpan<byte> traits, int[] groupOf, int g, string names)
    {
        for (var i = 0; i < groupOf.Length; i++)
        {
            if (groupOf[i] == g && (traits[i] & HostAliasGroups.Unsupported) != 0)
            {
                throw new NotSupportedException(
                    $"The arguments '{names}' overlap one memory, and the elements of some of them are converted " +
                    "between the host and the device layout, so they cannot share one copy.");
            }
        }
    }

    // The shift s past an aligned address at which every member m of group g starts aligned: (s + offset(m)) is a
    // multiple of the alignment of m. Alignments are powers of two, so the widest one fixes s and the others must agree
    private static nint GetShift(ReadOnlySpan<nint> ranges, int[] groupOf, int g, nint[] offsets, int alignment,
        string names)
    {
        nint shift = -1;
        for (var i = 0; i < groupOf.Length && shift < 0; i++)
        {
            if (groupOf[i] == g && GetAlignment(ranges[(i * HostAliasGroups.RangeFields) + 2]) == alignment)
            {
                shift = -offsets[i] & (alignment - 1);
            }
        }

        for (var i = 0; i < groupOf.Length; i++)
        {
            var alignmentOfMember = GetAlignment(ranges[(i * HostAliasGroups.RangeFields) + 2]);
            if (groupOf[i] == g && ((shift + offsets[i]) & (alignmentOfMember - 1)) != 0)
            {
                throw new NotSupportedException(
                    $"The arguments '{names}' overlap one memory at offsets that are not aligned for all of their " +
                    "element types, so the device cannot address them in one region.");
            }
        }

        return shift;
    }

    // The alignment of an element of the given size: its lowest set bit, at most MaxAlignment
    private static int GetAlignment(nint elementSize)
    {
        if (elementSize <= 0)
        {
            return 1;
        }

        var lowest = (int)(elementSize & -elementSize);
        return lowest is <= 0 or > MaxAlignment ? MaxAlignment : lowest;
    }

    internal static int Root(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    internal static bool HasOtherMember(int[] parent, int root)
    {
        for (var k = 0; k < parent.Length; k++)
        {
            if (k != root && Root(parent, k) == root)
            {
                return true;
            }
        }

        return false;
    }

    // The names of the members of group g, from the comma-separated parameter names
    private static string JoinNames(string names, int[] groupOf, int g)
    {
        var joined = "";
        var rest = names.AsSpan();
        for (var i = 0; i < groupOf.Length; i++)
        {
            var comma = rest.IndexOf(',');
            var name = comma < 0 ? rest : rest[..comma];
            rest = comma < 0 ? [] : rest[(comma + 1)..];
            if (groupOf[i] == g)
            {
                joined = joined.Length == 0 ? name.ToString() : $"{joined}', '{name}";
            }
        }

        return joined;
    }
}
