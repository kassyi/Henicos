namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The grouping of <see cref="HostAliasGroups.Find"/>: the extent, copy directions, conversion and device alignment of
/// each group of overlapping arguments, and the checks that a group can share one copy.
/// </summary>
internal static class HostAliasGrouping
{
    // The widest alignment that a device element needs (a 16-byte vector type)
    private const int MaxAlignment = 16;

    private const int F = HostAliasGroups.RangeFields;

    // The extent, the copy directions, the conversion and the device alignment of group g, and the host and device
    // offset of each member in it
    internal static HostAliasGroup BuildGroup(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, string names,
        int[] groupOf, int g, nint[] offsets, nint[] deviceOffsets)
    {
        var group = new HostAliasGroup { KernelGetsCopy = true, ViewsOnly = true, Alignment = 1, Leader = -1 };
        group.Names = JoinNames(names, groupOf, g);
        nint start = 0;
        nint end = 0;
        for (var i = 0; i < groupOf.Length; i++)
        {
            if (groupOf[i] != g)
            {
                continue;
            }

            var address = ranges[i * F];
            AddElementType(ref group, ranges, i);
            if (group.Leader < 0 || address < start)
            {
                group.Leader = i;
                start = address;
            }

            end = Math.Max(end, address + ranges[(i * F) + 1]);
            group.CopiesIn |= (traits[i] & HostAliasGroups.CopiesIn) != 0;
            group.CopiesBack |= (traits[i] & HostAliasGroups.CopiesBack) != 0;
            group.ViewsOnly &= (traits[i] & HostAliasGroups.SpanView) != 0;
            group.KernelGetsCopy &= (traits[i] & (HostAliasGroups.CopiesIn | HostAliasGroups.CopiesBack)) ==
                HostAliasGroups.CopiesIn;
            group.Alignment = Math.Max(group.Alignment, GetAlignment(ranges, i));
        }

        group.Length = end - start;
        SetOffsets(ref group, ranges, groupOf, g, start, offsets, deviceOffsets);
        group.Shift = GetShift(ranges, groupOf, g, deviceOffsets, group.Alignment, group.Names);
        return group;
    }

    // The conversion of the group is that of its first member; every other member must share it: the same conversion
    // (0 for bytes the device reads as they are) and, for a converted element, the same host and device sizes
    private static void AddElementType(ref HostAliasGroup group, ReadOnlySpan<nint> ranges, int i)
    {
        var key = (int)ranges[(i * F) + 5];
        var hostSize = ranges[(i * F) + 2];
        var deviceSize = ranges[(i * F) + 3];
        if (group.Leader < 0)
        {
            group.ConversionKey = key;
            group.HostElementSize = hostSize;
            group.DeviceElementSize = deviceSize;
            return;
        }

        if (key != group.ConversionKey ||
            (key != 0 && (hostSize != group.HostElementSize || deviceSize != group.DeviceElementSize)))
        {
            throw new NotSupportedException(
                $"The arguments '{group.Names}' overlap one memory, and the host and device layouts of their " +
                "elements differ in different ways, so no device region holds the bytes of all of them.");
        }
    }

    // The byte offset of each member from the start of the group, on the host and on the device: the same for bytes the
    // device reads as they are, scaled from host to device elements for a converted group
    private static void SetOffsets(ref HostAliasGroup group, ReadOnlySpan<nint> ranges, int[] groupOf, int g,
        nint start, nint[] offsets, nint[] deviceOffsets)
    {
        var converted = group.ConversionKey != 0;
        if (converted && group.Length % group.HostElementSize != 0)
        {
            throw NotAtElementBoundaries(group.Names);
        }

        group.DeviceLength = converted
            ? group.Length / group.HostElementSize * group.DeviceElementSize
            : group.Length;
        for (var i = 0; i < groupOf.Length; i++)
        {
            if (groupOf[i] != g)
            {
                continue;
            }

            offsets[i] = ranges[i * F] - start;
            if (converted && offsets[i] % group.HostElementSize != 0)
            {
                throw NotAtElementBoundaries(group.Names);
            }

            deviceOffsets[i] = converted ? offsets[i] / group.HostElementSize * group.DeviceElementSize : offsets[i];
        }
    }

    private static NotSupportedException NotAtElementBoundaries(string names) =>
        new($"The arguments '{names}' overlap one memory at offsets that are not element boundaries of their " +
            "converted elements, so no device region holds the elements of all of them.");

    // The shift s past an aligned address at which every member m of group g starts aligned: (s + offset(m)) is a
    // multiple of the alignment of m. Alignments are powers of two, so the widest one fixes s and the others must agree
    private static nint GetShift(ReadOnlySpan<nint> ranges, int[] groupOf, int g, nint[] deviceOffsets, int alignment,
        string names)
    {
        nint shift = -1;
        for (var i = 0; i < groupOf.Length && shift < 0; i++)
        {
            if (groupOf[i] == g && GetAlignment(ranges, i) == alignment)
            {
                shift = -deviceOffsets[i] & (alignment - 1);
            }
        }

        for (var i = 0; i < groupOf.Length; i++)
        {
            if (groupOf[i] == g && ((shift + deviceOffsets[i]) & (GetAlignment(ranges, i) - 1)) != 0)
            {
                throw new NotSupportedException(
                    $"The arguments '{names}' overlap one memory at offsets that are not aligned for all of their " +
                    "element types, so the device cannot address them in one region.");
            }
        }

        return shift;
    }

    // The device alignment of the elements of argument i: the one the launcher passes, or, when it passes 0, the
    // lowest set bit of the element size, at most MaxAlignment
    private static int GetAlignment(ReadOnlySpan<nint> ranges, int i)
    {
        var given = ranges[(i * F) + 4];
        if (given > 0)
        {
            return (int)Math.Min(given, MaxAlignment);
        }

        var elementSize = ranges[(i * F) + 2];
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
