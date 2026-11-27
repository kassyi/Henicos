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
        // An argument that is in no group (a struct array whose fields the launch compares) takes no group copy
        if (!groups.IsMember(index))
        {
            copy = null;
            start = 0;
            return false;
        }

        ref var group = ref groups.GroupAt(groups.GetGroup(index));
        if (!group.KernelGetsCopy)
        {
            copy = null;
            start = 0;
            return false;
        }

        var size = Unsafe.SizeOf<T>();
        var offset = groups.GetOffset(index);
        copy = group.HostCopy switch
        {
            null when IsAtBoundaries<T>(ref group, offset) => CopyGroup(ref group, ranges, copies, copyArrays),
            null when !RuntimeHelpers.IsReferenceOrContainsReferences<T>() => CopyGroupPadded<T>(ref group, ranges,
                (size - (int)(offset % size)) % size),
            null => throw NotAtElementBoundaries<T>(group.Names),
            T[] existing => existing,
            { } other when IsSameSizeWithoutReferences<T>(ref group) => Unsafe.As<T[]>(other),
            _ => throw DifferentElementTypes(group.Names)
        };
        // A buffer of bytes (CopyBytes) and a padded copy place the group at CopyStart; a typed copy at 0
        if ((group.CopyStart + offset) % size != 0)
        {
            throw NotAtElementBoundaries<T>(group.Names);
        }

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
        if (!groups.IsMember(index))
        {
            view = default;
            return false;
        }

        ref var group = ref groups.GroupAt(groups.GetGroup(index));
        if (!group.KernelGetsCopy)
        {
            view = default;
            return false;
        }

        var count = GetCount<T>(ranges, index);
        view = MemoryMarshal.CreateSpan(ref GetHostElement<T>(ref group, groups, index, ranges), count);
        return true;
    }

    /// <summary>
    /// Gets the reference that an <c>in</c> parameter at member <paramref name="index"/> passes to the kernel: the
    /// member's element in the group's copy (viewed as <typeparamref name="T"/> when the copy holds another element
    /// type without references), or <paramref name="current"/> when the group passes the caller's memory.
    /// </summary>
    /// <param name="groups">The groups of the launch.</param>
    /// <param name="index">The member.</param>
    /// <param name="ranges">
    /// The range list of <see cref="HostAliasGroups.Find"/>, with the arguments still pinned.
    /// </param>
    /// <param name="current">The caller's reference to fall back to when the group passes the caller's memory.</param>
    /// <typeparam name="T">The type of the <c>in</c> parameter.</typeparam>
    /// <exception cref="NotSupportedException">
    /// The group's copy holds references of another element type, or the member has references and does not start at
    /// an element boundary.
    /// </exception>
    public static ref readonly T GetHostReference<T>(HostAliasGroups groups, int index, ReadOnlySpan<nint> ranges,
        in T current)
    {
        if (!groups.IsMember(index))
        {
            return ref current;
        }

        ref var group = ref groups.GroupAt(groups.GetGroup(index));
        if (!group.KernelGetsCopy)
        {
            return ref current;
        }

        return ref GetHostElement<T>(ref group, groups, index, ranges);
    }

    // The first element of member <index> inside a KernelGetsCopy group's host copy, typed as T
    private static ref T GetHostElement<T>(ref HostAliasGroup group, HostAliasGroups groups, int index,
        ReadOnlySpan<nint> ranges)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            // Elements with references are never reinterpreted: one typed copy serves every member
            if (!TryGetHostCopy<T>(groups, index, ranges, out var copy, out var start))
            {
                throw new InvalidOperationException("Expected a copy for a KernelGetsCopy group member.");
            }

            return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(copy), start);
        }

        if (group.HostCopyHasReferences)
        {
            // The copy's elements hold references, whose bits no view of another element type may read or write
            throw DifferentElementTypes(group.Names);
        }

        var offset = groups.GetOffset(index);
        var data = group.HostCopy ?? (group.ViewsOnly || !IsAtBoundaries<T>(ref group, offset)
            ? (Array)CopyBytes(ref group, ranges)
            : CopyGroup<T>(ref group, ranges, null, null));
        return ref Unsafe.As<byte, T>(ref GetByte(ref group, data, offset));
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
                ? !group.HostCopyHasReferences &&
                  ((other is not T[] && !IsSameSizeWithoutReferences<T>(ref group)) ||
                   (group.CopyStart + offset) % Unsafe.SizeOf<T>() != 0)
                : !IsAtBoundaries<T>(ref group, offset)))
        {
            // A copy of another element type, one that the member does not start at an element of, or none yet that T
            // could type: the member views the copy's bytes
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

    // One typed copy of a group that the member does not start at an element boundary of, or whose length is no
    // multiple of the element: the group starts <pad> bytes into the copy, so that the member starts at an element, and
    // the bytes around the group stay zero; no member reads them. Elements without references only, as the bytes are
    // copied as they are
    // [WHY] A T[] member (ArraySegment<T>, ImmutableArray<T>) needs a T[] that its elements start in, which a copy
    // from the first byte of the group cannot give when another member starts before it at a smaller alignment (an
    // ArraySegment<int> after a Memory<short> that starts 2 bytes before it); CUDA aligns the region the same way
    private static unsafe T[] CopyGroupPadded<T>(ref HostAliasGroup group, ReadOnlySpan<nint> ranges, int pad)
    {
        var size = Unsafe.SizeOf<T>();
        var length = checked((int)group.Length);
        var created = new T[(pad + length + size - 1) / size];
        var source = (void*)ranges[group.Leader * HostAliasGroups.RangeFields];
        var bytes = MemoryMarshal.CreateSpan(
            ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(created)), created.Length * size);
        new ReadOnlySpan<byte>(source, length).CopyTo(bytes.Slice(pad));
        group.HostCopy = created;
        group.HostCopyElementSize = size;
        group.HostCopyHasReferences = false;
        group.CopyStart = pad;
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
