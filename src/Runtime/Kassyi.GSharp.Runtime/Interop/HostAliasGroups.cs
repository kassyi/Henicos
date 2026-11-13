using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
/// (<see cref="TryGetHostCopy{T}"/>, CPU). Members whose host and device layouts differ cannot share bytes, and members
/// whose offsets no region can align for all of them would be read misaligned on the device; both throw
/// <see cref="NotSupportedException"/> instead of running with wrong values.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostAliasGroups
{
    /// <summary>Trait: the launcher copies the argument in (CUDA), or the kernel reads the caller's values.</summary>
    public const byte CopiesIn = 1;

    /// <summary>Trait: the launcher copies the argument back, or the kernel's writes reach the caller.</summary>
    public const byte CopiesBack = 2;

    /// <summary>Trait: the argument cannot share a copy with another one (its elements are converted).</summary>
    public const byte Unsupported = 4;

    /// <summary>
    /// Trait: a <c>T[]</c> argument passed whole, whose sharing with another <c>T[]</c> of the same array the launcher
    /// already handles by reference identity.
    /// </summary>
    public const byte WholeArray = 8;

    /// <summary>
    /// Trait (CPU): a <c>Span&lt;T&gt;</c> or <c>ReadOnlySpan&lt;T&gt;</c> argument, which can view a copy of any
    /// element type (<see cref="TryGetHostView{T}"/>).
    /// </summary>
    public const byte SpanView = 16;

    // The alignment that a CPU copy keeps from the caller's memory: the widest element alignment
    private const int CopyAlignment = 16;

    /// <summary>The number of values per argument in a range list: address, byte length, element size.</summary>
    public const int RangeFields = 3;

    private readonly int[] _groupOf;
    private readonly nint[] _offsets;
    private readonly HostAliasGroup[] _groups;

    private HostAliasGroups(int[] groupOf, nint[] offsets, HostAliasGroup[] groups)
    {
        _groupOf = groupOf;
        _offsets = offsets;
        _groups = groups;
    }

    /// <summary>The number of groups, each of two or more arguments.</summary>
    public int GroupCount => _groups.Length;

    /// <summary>
    /// Finds the arguments that overlap. Returns <see langword="null"/> when none do, which is the common case.
    /// </summary>
    /// <param name="ranges">
    /// <see cref="RangeFields"/> values per argument: the pinned address, the length in bytes (0 for a null or empty
    /// argument) and the element size.
    /// </param>
    /// <param name="traits">The <see cref="CopiesIn"/>, <see cref="CopiesBack"/>, ... flags of each argument.</param>
    /// <param name="names">The parameter names, separated by commas, for the exception messages.</param>
    /// <exception cref="NotSupportedException">A group cannot share one copy (see the remarks of the type).</exception>
    public static HostAliasGroups? Find(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, string names)
    {
        var count = traits.Length;
        if (ranges.Length != count * RangeFields)
        {
            throw new ArgumentException("The range list does not hold three values per argument.", nameof(ranges));
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
        return new HostAliasGroupLayout(g.Length, g.Shift, g.Alignment, g.CopiesIn, g.CopiesBack);
    }

    /// <summary>
    /// CUDA: allocates one device region per group and copies the groups that any member copies in, then waits for the
    /// copies. The regions are freed by <see cref="FreeDevice"/>, also when this throws.
    /// </summary>
    /// <param name="ranges">The range list of <see cref="Find"/>, with the arguments still pinned.</param>
    /// <param name="stream">The stream of the launch.</param>
    public void CopyToDevice(ReadOnlySpan<nint> ranges, IntPtr stream)
    {
        var copied = false;
        for (var g = 0; g < _groups.Length; g++)
        {
            ref var group = ref _groups[g];
            var size = group.Length + (2 * group.Alignment);
            CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out group.Region, size, stream));
            group.Base = AlignUp(group.Region, group.Alignment) + group.Shift;
            if (!group.CopiesIn)
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
    public IntPtr GetDevicePointer(int index) => _groups[_groupOf[index]].Base + _offsets[index];

    /// <summary>
    /// CUDA: copies back the groups that any member copies back, then waits for the stream (also when nothing was
    /// copied, as the copy back of the members would have).
    /// </summary>
    /// <param name="ranges">A range list of the same arguments, pinned again: only the addresses are read.</param>
    /// <param name="stream">The stream of the launch.</param>
    public void CopyToHost(ReadOnlySpan<nint> ranges, IntPtr stream)
    {
        foreach (var group in _groups)
        {
            if (group.CopiesBack && group.Region != IntPtr.Zero)
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

    /// <summary>
    /// CPU: gets the copy that member <paramref name="index"/> passes to the kernel instead of the caller's memory,
    /// when every member of its group is copied in and none is copied back (all <c>[In]</c>): one copy of the whole
    /// group, made on the first call, that every member addresses at its offset. Returns <see langword="false"/> when
    /// the group passes the caller's memory (a member writes it back).
    /// </summary>
    /// <param name="index">The member.</param>
    /// <param name="ranges">The range list of <see cref="Find"/>, with the arguments still pinned.</param>
    /// <param name="copy">The copy of the group.</param>
    /// <param name="start">The element index of the member in <paramref name="copy"/>.</param>
    /// <typeparam name="T">The element type of the member.</typeparam>
    /// <exception cref="NotSupportedException">
    /// The members of the group have different element types, or do not start at element boundaries.
    /// </exception>
    public bool TryGetHostCopy<T>(int index, ReadOnlySpan<nint> ranges, [NotNullWhen(true)] out T[]? copy,
        out int start)
    {
        ref var group = ref _groups[_groupOf[index]];
        if (!group.KernelGetsCopy)
        {
            copy = null;
            start = 0;
            return false;
        }

        var size = Unsafe.SizeOf<T>();
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() || group.Length % size != 0 ||
            _offsets[index] % size != 0)
        {
            throw new NotSupportedException(
                $"The [In] arguments that overlap one memory with '{group.Names}' do not start at element boundaries " +
                $"of {typeof(T).Name}, so the kernel cannot get one copy of them.");
        }

        switch (group.HostCopy)
        {
            case null:
                var created = new T[group.Length / size];
                var source = ranges[group.Leader * RangeFields];
                unsafe
                {
                    new ReadOnlySpan<byte>((void*)source, checked((int)group.Length)).CopyTo(
                        MemoryMarshal.CreateSpan(
                            ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(created)),
                            checked((int)group.Length)
                        )
                    );
                }

                group.HostCopy = created;
                copy = created;
                break;
            case T[] existing:
                copy = existing;
                break;
            default:
                throw new NotSupportedException(
                    $"The [In] arguments that overlap one memory with '{group.Names}' have different element types, " +
                    "so the kernel cannot get one copy of them.");
        }

        start = (int)(_offsets[index] / size);
        return true;
    }

    /// <summary>
    /// CPU: gets the view that a <c>Span&lt;T&gt;</c> or <c>ReadOnlySpan&lt;T&gt;</c> member passes to the kernel
    /// instead of the caller's memory, under the same rule as <see cref="TryGetHostCopy{T}"/>. When every member of the
    /// group is such a span, the copy is one buffer of bytes placed at the caller's alignment, so members of different
    /// element types (an <c>int</c> and a <c>long</c> view of one array) view the same bytes, as on the device.
    /// </summary>
    /// <param name="index">The member.</param>
    /// <param name="ranges">The range list of <see cref="Find"/>, with the arguments still pinned.</param>
    /// <param name="view">The member's elements in the copy.</param>
    /// <typeparam name="T">The element type of the member.</typeparam>
    /// <exception cref="NotSupportedException">
    /// The group also holds an array, <c>Memory&lt;T&gt;</c> or segment of another element type, which needs a typed
    /// copy.
    /// </exception>
    public bool TryGetHostView<T>(int index, ReadOnlySpan<nint> ranges, out Span<T> view)
    {
        ref var group = ref _groups[_groupOf[index]];
        var size = Unsafe.SizeOf<T>();
        var count = checked((int)(ranges[(index * RangeFields) + 1] / size));
        if (!group.ViewsOnly)
        {
            var found = TryGetHostCopy<T>(index, ranges, out var copy, out var start);
            view = found ? copy.AsSpan(start, count) : default;
            return found;
        }

        if (!group.KernelGetsCopy)
        {
            view = default;
            return false;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new NotSupportedException(
                $"The [In] arguments that overlap one memory with '{group.Names}' hold references.");
        }

        var bytes = group.HostCopy as byte[] ?? CopyBytes(ref group, ranges);
        ref var data = ref MemoryMarshal.GetArrayDataReference(bytes);
        ref var first = ref Unsafe.Add(ref data, group.CopyStart + _offsets[index]);
        view = MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref first), count);
        return true;
    }

    // One pinned byte buffer for a group of span views, whose copy of the group starts at the address that the caller's
    // memory has modulo CopyAlignment, so that every view keeps the alignment of its elements
    private static unsafe byte[] CopyBytes(ref HostAliasGroup group, ReadOnlySpan<nint> ranges)
    {
        var length = checked((int)group.Length);
        var bytes = GC.AllocateUninitializedArray<byte>(length + CopyAlignment, pinned: true);
        var source = ranges[group.Leader * RangeFields];
        var buffer = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(bytes));
        var start = (int)((source - buffer) & (CopyAlignment - 1));
        new ReadOnlySpan<byte>((void*)source, length).CopyTo(bytes.AsSpan(start, length));
        group.HostCopy = bytes;
        group.CopyStart = start;
        return bytes;
    }

    // Whether arguments i and j belong to one group: both hold bytes, and their bytes overlap. Two T[] arguments that
    // are one array (the same range) are left to the launcher's identity sharing, unless a third argument joins them
    private static bool Joins(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, int i, int j)
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

    private static HostAliasGroups Build(ReadOnlySpan<nint> ranges, ReadOnlySpan<byte> traits, string names)
    {
        var count = traits.Length;
        var parent = new int[count];
        for (var i = 0; i < count; i++)
        {
            parent[i] = i;
        }

        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (Joins(ranges, traits, i, j))
                {
                    parent[HostAliasGrouping.Root(parent, j)] = HostAliasGrouping.Root(parent, i);
                }
            }
        }

        var groupOf = new int[count];
        var groupOfRoot = new int[count];
        groupOfRoot.AsSpan().Fill(-1);
        var groupCount = 0;
        for (var i = 0; i < count; i++)
        {
            groupOf[i] = -1;
            var root = HostAliasGrouping.Root(parent, i);
            if (root == i && !HostAliasGrouping.HasOtherMember(parent, i))
            {
                continue;
            }

            if (groupOfRoot[root] < 0)
            {
                groupOfRoot[root] = groupCount++;
            }

            groupOf[i] = groupOfRoot[root];
        }

        var groups = new HostAliasGroup[groupCount];
        var offsets = new nint[count];
        for (var g = 0; g < groupCount; g++)
        {
            groups[g] = HostAliasGrouping.BuildGroup(ranges, traits, names, groupOf, g, offsets);
        }

        return new HostAliasGroups(groupOf, offsets, groups);
    }

    private static IntPtr AlignUp(IntPtr address, int alignment) =>
        (address + (alignment - 1)) & ~(nint)(alignment - 1);
}
