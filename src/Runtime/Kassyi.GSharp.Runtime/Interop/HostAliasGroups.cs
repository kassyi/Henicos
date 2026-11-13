using System.ComponentModel;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The arguments of one launch that share host memory: overlapping slices of one array, a <c>ref</c> to an element
/// and the array itself, one variable passed to two <c>ref</c> parameters. In C# they are one memory, so a write
/// through one argument is seen through the others; the launcher gives each group one copy that its members address
/// at their offsets.
/// </summary>
/// <remarks>
/// <para>
/// The generated launcher pins its candidate arguments, writes each one's address, byte length and element size into
/// a range list, and calls <see cref="Find"/>. Without an overlap it gets <see langword="null"/> and runs as before, so
/// a launch without aliases pays for the comparisons only. Every group lies inside one object (or one variable on the
/// stack), so the offsets of its members stay valid after the arguments are unpinned.
/// </para>
/// <para>
/// A group is one holder: it is copied in when any member is copied in and copied back when any member is copied back,
/// so an <c>[In]</c> member keeps its own copy only when every member is <c>[In]</c>
/// (<see cref="HostAliasCopies"/>, CPU). A group whose elements the launcher converts between the host and the
/// device layout holds them converted (<see cref="HostAliasStaging"/>). Members whose layouts differ in different ways
/// cannot share one region, and members whose offsets no region can align for all of them would be read misaligned on
/// the device; both throw <see cref="NotSupportedException"/> instead of running with wrong values.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostAliasGroups
{
    /// <summary>Trait: the launcher copies the argument in (CUDA), or the kernel reads the caller's values.</summary>
    public const byte CopiesIn = 1;

    /// <summary>Trait: the launcher copies the argument back, or the kernel's writes reach the caller.</summary>
    public const byte CopiesBack = 2;

    /// <summary>
    /// Trait: a <c>T[]</c> argument passed whole, whose sharing with another <c>T[]</c> of the same array the launcher
    /// already handles by reference identity.
    /// </summary>
    public const byte WholeArray = 8;

    /// <summary>
    /// Trait (CPU): a <c>Span&lt;T&gt;</c> or <c>ReadOnlySpan&lt;T&gt;</c> argument, which can view a copy of any
    /// element type (<see cref="HostAliasCopies.TryGetHostView{T}"/>).
    /// </summary>
    public const byte SpanView = 16;

    /// <summary>
    /// The number of values per argument in a range list: the pinned address, the length in bytes, the host element
    /// size, the device element size, the device alignment of the elements (0: derived from the size) and the
    /// conversion (0 when the device reads the bytes as they are, otherwise one number per converted element type).
    /// </summary>
    public const int RangeFields = 6;

    private readonly int[] _groupOf;
    private readonly nint[] _offsets;
    private readonly nint[] _deviceOffsets;
    private readonly HostAliasGroup[] _groups;

    private HostAliasGroups(int[] groupOf, nint[] offsets, nint[] deviceOffsets, HostAliasGroup[] groups)
    {
        _groupOf = groupOf;
        _offsets = offsets;
        _deviceOffsets = deviceOffsets;
        _groups = groups;
    }

    /// <summary>The number of groups, each of two or more arguments.</summary>
    public int GroupCount => _groups.Length;

    /// <summary>
    /// Finds the arguments that overlap. Returns <see langword="null"/> when none do, which is the common case.
    /// </summary>
    /// <param name="ranges">
    /// <see cref="RangeFields"/> values per argument (the length in bytes is 0 for a null or empty argument).
    /// </param>
    /// <param name="traits">The <see cref="CopiesIn"/>, <see cref="CopiesBack"/>, ... flags of each argument.</param>
    /// <param name="names">The parameter names, separated by commas, for the exception messages.</param>
    /// <exception cref="NotSupportedException">A group cannot share one copy (see the remarks of the type).</exception>
    public static HostAliasGroups? Find(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, string names)
    {
        var count = traits.Length;
        if (ranges.Length != count * RangeFields)
        {
            throw new ArgumentException(
                "The range list does not hold RangeFields values per argument.", nameof(ranges));
        }

        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (Joins(ranges, traits, i, j))
                {
                    return Build(ranges, traits, names);
                }
            }
        }

        return null;
    }

    /// <summary>Whether argument <paramref name="index"/> belongs to a group.</summary>
    public bool IsMember(int index) => _groupOf[index] >= 0;

    /// <summary>The byte offset of member <paramref name="index"/> from the start of its group.</summary>
    public nint GetOffset(int index) => _offsets[index];

    /// <summary>The group of member <paramref name="index"/>, or -1.</summary>
    public int GetGroup(int index) => _groupOf[index];

    /// <summary>The extent, alignment and copy directions of group <paramref name="group"/>.</summary>
    public HostAliasGroupLayout GetGroupLayout(int group)
    {
        ref readonly var g = ref _groups[group];
        return new HostAliasGroupLayout(g.Length, g.Shift, g.Alignment, g.CopiesIn, g.CopiesBack, g.ConversionKey);
    }

    // The state of group g and the device offset of member index, for HostAliasStaging
    internal ref HostAliasGroup GroupAt(int g) => ref _groups[g];

    internal nint GetDeviceOffset(int index) => _deviceOffsets[index];

    /// <summary>
    /// CUDA: allocates one device region per group and copies the groups that any member copies in, then waits for the
    /// copies. A converted group is filled by the launcher through <see cref="HostAliasStaging"/> instead. The regions
    /// are freed by <see cref="FreeDevice"/>, also when this throws.
    /// </summary>
    /// <param name="ranges">The range list of <see cref="Find"/>, with the arguments still pinned.</param>
    /// <param name="stream">The stream of the launch.</param>
    public void CopyToDevice(ReadOnlySpan<nint> ranges, IntPtr stream)
    {
        var copied = false;
        for (var g = 0; g < _groups.Length; g++)
        {
            ref var group = ref _groups[g];
            var size = group.DeviceLength + (2 * group.Alignment);
            CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out group.Region, size, stream));
            group.Base = AlignUp(group.Region, group.Alignment) + group.Shift;
            if (!group.CopiesIn || group.ConversionKey != 0)
            {
                continue;
            }

            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(
                group.Base, ranges[group.Leader * RangeFields], group.Length, stream));
            copied = true;
        }

        if (copied)
        {
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));
        }
    }

    /// <summary>CUDA: the device address of member <paramref name="index"/> after <see cref="CopyToDevice"/>.</summary>
    public IntPtr GetDevicePointer(int index) => _groups[_groupOf[index]].Base + _deviceOffsets[index];

    /// <summary>
    /// CUDA: copies back the groups that any member copies back, then waits for the stream (also when nothing was
    /// copied, as the copy back of the members would have). A converted group goes back through
    /// <see cref="HostAliasStaging"/>.
    /// </summary>
    /// <param name="ranges">A range list of the same arguments, pinned again: only the addresses are read.</param>
    /// <param name="stream">The stream of the launch.</param>
    public void CopyToHost(ReadOnlySpan<nint> ranges, IntPtr stream)
    {
        foreach (var group in _groups)
        {
            if (group.CopiesBack && group.Region != IntPtr.Zero && group.ConversionKey == 0)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(
                    ranges[group.Leader * RangeFields], group.Base, group.Length, stream));
            }
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));
    }

    /// <summary>CUDA: frees the device regions of the groups on <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream of the launch.</param>
    public void FreeDevice(IntPtr stream)
    {
        for (var g = 0; g < _groups.Length; g++)
        {
            ref var group = ref _groups[g];
            if (group.Region != IntPtr.Zero)
            {
                // [WHY] Runs in the launcher's finally block: a failed free must not hide the launch's own exception
                _ = CudaDriverApi.cuMemFreeAsync(group.Region, stream);
                group.Region = IntPtr.Zero;
            }
        }
    }

    // Whether arguments i and j belong to one group: both hold bytes, and their bytes overlap. Two T[] arguments that
    // are one array (the same range) are left to the launcher's identity sharing, unless a third argument joins them
    internal static bool Joins(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, int i, int j)
    {
        var a = ranges[i * RangeFields];
        var aLength = ranges[(i * RangeFields) + 1];
        var b = ranges[j * RangeFields];
        var bLength = ranges[(j * RangeFields) + 1];
        if (aLength <= 0 || bLength <= 0 || a - b >= bLength || b - a >= aLength)
        {
            return false;
        }

        return (traits[i] & traits[j] & WholeArray) == 0 || a != b || aLength != bLength;
    }

    // The groups: the overlapping arguments joined (HostAliasGrouping.Unite), the groups numbered, then each group laid
    // out on the host and the device
    private static HostAliasGroups Build(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, string names)
    {
        var count = traits.Length;
        var parent = HostAliasGrouping.Unite(ranges, traits);
        var groupOf = HostAliasGrouping.NumberGroups(parent, out var groupCount);
        var groups = new HostAliasGroup[groupCount];
        var offsets = new nint[count];
        var deviceOffsets = new nint[count];
        for (var g = 0; g < groupCount; g++)
        {
            groups[g] = HostAliasGrouping.BuildGroup(ranges, traits, names, groupOf, g, offsets, deviceOffsets);
        }

        return new HostAliasGroups(groupOf, offsets, deviceOffsets, groups);
    }

    private static IntPtr AlignUp(IntPtr address, int alignment) =>
        (address + (alignment - 1)) & ~(nint)(alignment - 1);
}
