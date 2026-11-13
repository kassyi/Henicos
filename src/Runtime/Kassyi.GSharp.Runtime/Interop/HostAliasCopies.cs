using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// CPU: the copy that the members of a group of overlapping arguments (<see cref="HostAliasGroups"/>) pass to the
/// kernel instead of the caller's memory, when every member is copied in and none is copied back (all <c>[In]</c>).
/// The group has one copy, made on the first request, that every member addresses at its offset, so a write through
/// one member is seen through the others, as in the device region.
/// </summary>
/// <remarks>
/// The copy is typed by the first member that needs an array (a <c>T[]</c>, <c>ArraySegment&lt;T&gt;</c>,
/// <c>ImmutableArray&lt;T&gt;</c> or a <c>Memory&lt;T&gt;</c>); a member of another element type views it: a span
/// reinterprets its bytes, a <c>Memory&lt;T&gt;</c> goes through a <see cref="HostAliasMemoryManager{T}"/>, and an
/// array of another element type of the same size (an <c>int[]</c> passed as a <c>uint[]</c>, which the CLR allows) is
/// the same copy reinterpreted. A group without such an array (spans and memories only) shares a buffer of bytes placed
/// at the caller's alignment when its members do not all start at boundaries of the first member's elements.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HostAliasCopies
{
    // The alignment that a buffer of bytes keeps from the caller's memory: the widest element alignment
    private const int CopyAlignment = 16;

    /// <summary>
    /// Gets the copy of the group of member <paramref name="index"/>, typed by <typeparamref name="T"/>. Returns
    /// <see langword="false"/> when the group passes the caller's memory (a member writes it back).
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="index">The member.</param>
    /// <param name="ranges">
    /// The range list of <see cref="HostAliasGroups.Find"/>, with the arguments still pinned.
    /// </param>
    /// <param name="copy">The copy of the group.</param>
    /// <param name="start">The element index of the member in <paramref name="copy"/>.</param>
    /// <param name="copies">
    /// The launch's copies of the arrays that elements hold, for elements with array fields; otherwise null.
    /// </param>
    /// <param name="copyArrays">
    /// The generated <c>CopyArrays_</c> of the element, which gives the copy's elements copies of their arrays, so that
    /// the kernel's writes through them do not reach the caller either; null for elements without array fields.
    /// </param>
    /// <typeparam name="T">The element type of the member.</typeparam>
    /// <exception cref="NotSupportedException">
    /// The group's copy has elements of another size or with references, or the member does not start at an element
    /// boundary.
    /// </exception>
    public static bool TryGetHostCopy<T>(HostAliasGroups groups, int index, ReadOnlySpan<nint> ranges,
        [NotNullWhen(true)] out T[]? copy, out int start, HostArrayCopies? copies = null,
        HostArrayElementCopy<T>? copyArrays = null)
    {
        ref var group = ref groups.GroupAt(groups.GetGroup(index));
        if (!group.KernelGetsCopy)
        {
            copy = null;
            start = 0;
            return false;
        }

        var size = Unsafe.SizeOf<T>();
        var offset = groups.GetOffset(index);
        if (!IsAtBoundaries<T>(ref group, offset))
        {
            throw NotAtElementBoundaries<T>(group.Names);
        }

        copy = group.HostCopy switch
        {
            null => CopyGroup(ref group, ranges, copies, copyArrays),
            T[] existing => existing,
            { } other when IsSameSizeWithoutReferences<T>(ref group) => Unsafe.As<T[]>(other),
            _ => throw DifferentElementTypes(group.Names)
        };
        // A buffer of bytes (CopyBytes) places the group at CopyStart; a typed copy at 0
        start = (int)((group.CopyStart + offset) / size);
        return true;
    }

    /// <summary>
    /// Gets the view that a <c>Span&lt;T&gt;</c> or <c>ReadOnlySpan&lt;T&gt;</c> member passes to the kernel, under
    /// the rule of <see cref="TryGetHostCopy{T}"/>: the bytes of the group's copy at the member's offset, viewed as
    /// <typeparamref name="T"/>, whatever the element type of the copy.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="index">The member.</param>
    /// <param name="ranges">
    /// The range list of <see cref="HostAliasGroups.Find"/>, with the arguments still pinned.
    /// </param>
    /// <param name="view">The member's elements in the copy.</param>
    /// <typeparam name="T">The element type of the member.</typeparam>
    public static bool TryGetHostView<T>(HostAliasGroups groups, int index, ReadOnlySpan<nint> ranges,
        out Span<T> view)
    {
        ref var group = ref groups.GroupAt(groups.GetGroup(index));
        var count = GetCount<T>(ranges, index);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            // Elements with references are never reinterpreted: one typed copy serves every member
            var found = TryGetHostCopy<T>(groups, index, ranges, out var copy, out var start);
            view = found ? copy.AsSpan(start, count) : default;
            return found;
        }

        if (!group.KernelGetsCopy)
        {
            view = default;
            return false;
        }

        var offset = groups.GetOffset(index);
        var data = group.HostCopy ?? (group.ViewsOnly || !IsAtBoundaries<T>(ref group, offset)
            ? (Array)CopyBytes(ref group, ranges)
            : CopyGroup<T>(ref group, ranges, null, null));
        ref var first = ref Unsafe.As<byte, T>(ref GetByte(ref group, data, offset));
        view = MemoryMarshal.CreateSpan(ref first, count);
        return true;
    }

    /// <summary>
    /// Gets the <c>Memory&lt;T&gt;</c> that a <c>Memory&lt;T&gt;</c> or <c>ReadOnlyMemory&lt;T&gt;</c> member passes to
    /// the kernel, under the rule of <see cref="TryGetHostCopy{T}"/>: over the copy itself when it holds
    /// <typeparamref name="T"/> elements (or same-size ones without references), otherwise through a
    /// <see cref="HostAliasMemoryManager{T}"/> over the copy's bytes.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="index">The member.</param>
    /// <param name="ranges">
    /// The range list of <see cref="HostAliasGroups.Find"/>, with the arguments still pinned.
    /// </param>
    /// <param name="memory">The member's elements in the copy.</param>
    /// <param name="copies">As for <see cref="TryGetHostCopy{T}"/>.</param>
    /// <param name="copyArrays">As for <see cref="TryGetHostCopy{T}"/>.</param>
    /// <typeparam name="T">The element type of the member.</typeparam>
    public static bool TryGetHostMemory<T>(HostAliasGroups groups, int index, ReadOnlySpan<nint> ranges,
        out Memory<T> memory, HostArrayCopies? copies = null, HostArrayElementCopy<T>? copyArrays = null)
    {
        ref var group = ref groups.GroupAt(groups.GetGroup(index));
        var count = GetCount<T>(ranges, index);
        var offset = groups.GetOffset(index);
        if (group.KernelGetsCopy && !RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            (group.HostCopy is { } other
                ? other is not T[] && !IsSameSizeWithoutReferences<T>(ref group)
                : !IsAtBoundaries<T>(ref group, offset)))
        {
            // A copy of another element type, or none yet that T could type: the member views the copy's bytes
            var data = group.HostCopy ?? CopyBytes(ref group, ranges);
            var byteOffset = checked((int)(group.CopyStart + offset));
            memory = new HostAliasMemoryManager<T>(data, byteOffset, count).Memory;
            return true;
        }

        var found = TryGetHostCopy(groups, index, ranges, out var copy, out var start, copies, copyArrays);
        memory = found ? new Memory<T>(copy, start, count) : default;
        return found;
    }

    private static int GetCount<T>(ReadOnlySpan<nint> ranges, int index) =>
        checked((int)(ranges[(index * HostAliasGroups.RangeFields) + 1] / Unsafe.SizeOf<T>()));

    // The first byte of member <offset> in the group's copy
    private static ref byte GetByte(ref HostAliasGroup group, Array data, nint offset) =>
        ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), checked((int)(group.CopyStart + offset)));

    // Whether a copy of T elements covers the whole group and member <offset> starts at one of its elements
    private static bool IsAtBoundaries<T>(ref HostAliasGroup group, nint offset) =>
        group.Length % Unsafe.SizeOf<T>() == 0 && offset % Unsafe.SizeOf<T>() == 0;

    private static bool IsSameSizeWithoutReferences<T>(ref HostAliasGroup group) =>
        !group.HostCopyHasReferences && !RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
        group.HostCopyElementSize == Unsafe.SizeOf<T>();

    // One typed copy of the whole group, element by element (with write barriers for elements that hold references),
    // whose elements get copies of the arrays they hold when copyArrays is given
    private static unsafe T[] CopyGroup<T>(ref HostAliasGroup group, ReadOnlySpan<nint> ranges,
        HostArrayCopies? copies, HostArrayElementCopy<T>? copyArrays)
    {
        var created = new T[group.Length / Unsafe.SizeOf<T>()];
        var source = (void*)ranges[group.Leader * HostAliasGroups.RangeFields];
        MemoryMarshal.CreateSpan(ref Unsafe.AsRef<T>(source), created.Length).CopyTo(created);
        if (copyArrays is not null && copies is not null)
        {
            for (var i = 0; i < created.Length; i++)
            {
                copyArrays(ref created[i], copies);
            }
        }

        group.HostCopy = created;
        group.HostCopyElementSize = Unsafe.SizeOf<T>();
        group.HostCopyHasReferences = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        return created;
    }

    // One pinned byte buffer for a group of views, whose copy of the group starts at the address that the caller's
    // memory has modulo CopyAlignment, so that every view keeps the alignment of its elements
    private static unsafe byte[] CopyBytes(ref HostAliasGroup group, ReadOnlySpan<nint> ranges)
    {
        var length = checked((int)group.Length);
        var bytes = GC.AllocateUninitializedArray<byte>(length + CopyAlignment, pinned: true);
        var source = ranges[group.Leader * HostAliasGroups.RangeFields];
        var buffer = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(bytes));
        var start = (int)((source - buffer) & (CopyAlignment - 1));
        new ReadOnlySpan<byte>((void*)source, length).CopyTo(bytes.AsSpan(start, length));
        group.HostCopy = bytes;
        group.HostCopyElementSize = 1;
        group.CopyStart = start;
        return bytes;
    }

    private static NotSupportedException NotAtElementBoundaries<T>(string names) =>
        new($"The [In] arguments that overlap one memory with '{names}' do not start at element boundaries of " +
            $"{typeof(T).Name}, so the kernel cannot get one copy of them.");

    private static NotSupportedException DifferentElementTypes(string names) =>
        new($"The [In] arguments that overlap one memory with '{names}' are arrays of element types of different " +
            "sizes or with references, so no one array is a copy of all of them.");
}
