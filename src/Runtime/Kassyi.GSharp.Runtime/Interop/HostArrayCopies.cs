using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The copies that one CPU launch gives a kernel instead of the caller's arrays and views, one copy per host array or
/// overlapping group: the <c>[In]</c> array arguments and the arrays and views that the elements of an <c>[In]</c> or
/// read-only container hold.
/// </summary>
/// <remarks>
/// In C# two holders of one array are one object, and on CUDA they share one device region. So every holder of an
/// array gets the same copy (#985), and an array that a writable argument holds is not copied at all
/// (<see cref="Keep"/>): the kernel writes it as the device writes the region that the writable holder copies back.
/// Views held in struct fields participate through <see cref="ShareView{T}"/>, <see cref="KeepView{T}"/>, and
/// <see cref="CopyView{T}(ref Memory{T})"/> keyed by pinned host address, byte length, and element type while the
/// launcher's <see cref="HostAliasHeldArrays"/> keeps them pinned (#1849). The CPU launcher creates the table only when
/// a container's elements hold arrays or views.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostArrayCopies
{
    private readonly Dictionary<object, object> _copies = new(ReferenceEqualityComparer.Instance);
    private Dictionary<(IntPtr Address, nint ByteLength, Type ElementType), object?>? _viewCopies;

    /// <summary>
    /// Records the copy of <paramref name="array"/> that another owner made (the copy of a group of overlapping
    /// arguments), so that every holder of the array takes it.
    /// </summary>
    /// <param name="array">The host array.</param>
    /// <param name="copy">The copy of the whole array.</param>
    public void Share(object array, object copy)
    {
        _copies.TryAdd(array, copy);
        _copies.TryAdd(copy, copy);
    }

    /// <summary>Records an array that the kernel gets itself, so that no holder copies it.</summary>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    public void Keep(object? array)
    {
        if (array is not null)
        {
            _copies.TryAdd(array, array);
        }
    }

    /// <summary>
    /// Records the shared group copy for a view at (<paramref name="hostAddress"/>, <paramref name="byteLength"/>)
    /// of element type <typeparamref name="T"/>, so that <see cref="CopyView{T}(ref Memory{T})"/> replaces it with
    /// <paramref name="memory"/>.
    /// </summary>
    public void ShareView<T>(IntPtr hostAddress, nint byteLength, Memory<T> memory)
    {
        _viewCopies ??= new Dictionary<(IntPtr, nint, Type), object?>();
        _viewCopies.TryAdd((hostAddress, byteLength, typeof(T)), memory);
    }

    /// <summary>
    /// Records that a view at (<paramref name="hostAddress"/>, <paramref name="byteLength"/>) of element type
    /// <typeparamref name="T"/> belongs to a group with a writer and must keep the caller's memory without copying.
    /// </summary>
    public void KeepView<T>(IntPtr hostAddress, nint byteLength)
    {
        _viewCopies ??= new Dictionary<(IntPtr, nint, Type), object?>();
        _viewCopies.TryAdd((hostAddress, byteLength, typeof(T)), null);
    }

    /// <summary>
    /// Replaces <paramref name="array"/> with its copy for the kernel: the copy another holder already made, the array
    /// itself when it is kept, or a new copy.
    /// </summary>
    /// <param name="array">The array, replaced in place; <see langword="null"/> stays <see langword="null"/>.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>
    /// Whether the copy is new, so that the caller copies the arrays its elements hold; a copy made before, or a kept
    /// array, has been handled already.
    /// </returns>
    public bool Copy<T>([NotNullIfNotNull(nameof(array))] ref T[]? array)
    {
        if (array is null)
        {
            return false;
        }

        if (_copies.TryGetValue(array, out var existing))
        {
            array = (T[])existing;
            return false;
        }

        var copy = (T[])array.Clone();
        _copies.Add(array, copy);
        // The copy is its own copy: a holder reached through the copy keeps it
        _copies.Add(copy, copy);
        array = copy;
        return true;
    }

    /// <summary>
    /// Replaces a <c>Memory&lt;T&gt;</c> with the group's shared copy (<see cref="ShareView{T}"/>), keeps the caller's
    /// view (<see cref="KeepView{T}"/>), or clones it for a kernel whose writes the caller must not see.
    /// </summary>
    /// <param name="memory">The view, replaced in place; a view over no memory stays as it is.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>Whether the view was copied, so that the caller copies the arrays its elements hold.</returns>
    public bool CopyView<T>(ref Memory<T> memory)
    {
        if (ArrayPinning.IsNull(memory.Span))
        {
            return false;
        }

        if (TryGetSharedView(memory.Span, out var hasShared, out var shared))
        {
            if (hasShared)
            {
                memory = shared;
            }

            return false;
        }

        memory = memory.ToArray();
        return true;
    }

    /// <summary>
    /// Replaces an <c>ArraySegment&lt;T&gt;</c> with the group's shared copy (<see cref="ShareView{T}"/>), keeps the
    /// caller's segment (<see cref="KeepView{T}"/>), or clones it for a kernel whose writes the caller must not see.
    /// </summary>
    /// <param name="segment">The segment, replaced in place; a segment over no array stays as it is.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>Whether the segment was copied, so that the caller copies the arrays its elements hold.</returns>
    /// <exception cref="NotSupportedException">
    /// The shared group copy is backed by a <see cref="HostAliasMemoryManager{T}"/> rather than a <typeparamref name="T"/> array.
    /// </exception>
    public bool CopyView<T>(ref ArraySegment<T> segment)
    {
        if (segment.Array is null)
        {
            return false;
        }

        if (TryGetSharedView(segment.AsSpan(), out var hasShared, out var shared))
        {
            if (hasShared)
            {
                if (!MemoryMarshal.TryGetArray<T>(shared, out var sharedSegment))
                {
                    throw new NotSupportedException(
                        "An [In] ArraySegment<T> field that overlaps an argument of another element size or " +
                        "alignment cannot view the shared group copy.");
                }

                segment = sharedSegment;
            }

            return false;
        }

        segment = new ArraySegment<T>(segment.AsSpan().ToArray());
        return true;
    }

    /// <summary>
    /// Replaces a <c>ReadOnlyMemory&lt;T&gt;</c> with the group's shared copy (<see cref="ShareView{T}"/>) or keeps the
    /// caller's view (<see cref="KeepView{T}"/>); an unaliased view whose elements hold no arrays stays as it is.
    /// </summary>
    /// <param name="memory">The view, replaced in place when aliased.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns><see langword="false"/>, as no elements hold arrays to walk.</returns>
    public bool CopyView<T>(ref ReadOnlyMemory<T> memory)
    {
        _ = TryReplaceSharedReadOnlyMemory(ref memory);
        return false;
    }

    /// <summary>
    /// Replaces a <c>ReadOnlyMemory&lt;T&gt;</c> with the group's shared copy (<see cref="ShareView{T}"/>), keeps the
    /// caller's view (<see cref="KeepView{T}"/>), or clones it into <paramref name="copy"/> so the caller can copy the
    /// arrays its elements hold without mutating the caller's backing array.
    /// </summary>
    /// <param name="memory">The view, replaced in place.</param>
    /// <param name="copy">The cloned backing array when copied, otherwise <see langword="null"/>.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>Whether the view was cloned into <paramref name="copy"/>.</returns>
    public bool CopyView<T>(ref ReadOnlyMemory<T> memory, [NotNullWhen(true)] out T[]? copy)
    {
        if (ArrayPinning.IsNull(memory.Span) ||
            TryReplaceSharedReadOnlyMemory(ref memory) ||
            memory.IsEmpty)
        {
            copy = null;
            return false;
        }

        copy = memory.Span.ToArray();
        memory = copy;
        return true;
    }

    /// <summary>
    /// Replaces an <c>ImmutableArray&lt;T&gt;</c> with the group's shared copy (<see cref="ShareView{T}"/>) or keeps
    /// the caller's array (<see cref="KeepView{T}"/>); an unaliased array whose elements hold no arrays stays as it is.
    /// </summary>
    /// <param name="array">The array, replaced in place when aliased.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns><see langword="false"/>, as no elements hold arrays to walk.</returns>
    /// <exception cref="NotSupportedException">
    /// The shared group copy is not a single <typeparamref name="T"/> array of the same length.
    /// </exception>
    public bool CopyView<T>(ref ImmutableArray<T> array)
    {
        _ = TryReplaceSharedImmutableArray(ref array);
        return false;
    }

    /// <summary>
    /// Replaces an <c>ImmutableArray&lt;T&gt;</c> with the group's shared copy (<see cref="ShareView{T}"/>), keeps the
    /// caller's array (<see cref="KeepView{T}"/>), or clones it into <paramref name="copy"/> so the caller can copy the
    /// arrays its elements hold without mutating the caller's backing array.
    /// </summary>
    /// <param name="array">The array, replaced in place.</param>
    /// <param name="copy">The cloned backing array when copied, otherwise <see langword="null"/>.</param>
    /// <typeparam name="T">The element type.</typeparam>
    /// <returns>Whether the array was cloned into <paramref name="copy"/>.</returns>
    /// <exception cref="NotSupportedException">
    /// The shared group copy is not a single <typeparamref name="T"/> array of the same length.
    /// </exception>
    public bool CopyView<T>(ref ImmutableArray<T> array, [NotNullWhen(true)] out T[]? copy)
    {
        if (array.IsDefaultOrEmpty || TryReplaceSharedImmutableArray(ref array))
        {
            copy = null;
            return false;
        }

        copy = array.AsSpan().ToArray();
        array = ImmutableCollectionsMarshal.AsImmutableArray(copy);
        return true;
    }

    private bool TryReplaceSharedReadOnlyMemory<T>(ref ReadOnlyMemory<T> memory)
    {
        if (ArrayPinning.IsNull(memory.Span) ||
            !TryGetSharedView(memory.Span, out var hasShared, out var shared))
        {
            return false;
        }

        if (hasShared)
        {
            memory = shared;
        }

        return true;
    }

    private bool TryReplaceSharedImmutableArray<T>(ref ImmutableArray<T> array)
    {
        if (array.IsDefaultOrEmpty || !TryGetSharedView(array.AsSpan(), out var hasShared, out var shared))
        {
            return false;
        }

        if (hasShared)
        {
            if (!MemoryMarshal.TryGetArray<T>(shared, out var segment) ||
                segment.Array is not { } sharedArray ||
                segment.Offset != 0 ||
                segment.Count != sharedArray.Length)
            {
                throw new NotSupportedException(
                    "An [In] ImmutableArray<T> field that overlaps an argument of another element size or " +
                    "alignment cannot view the shared group copy.");
            }

            array = ImmutableCollectionsMarshal.AsImmutableArray(sharedArray);
        }

        return true;
    }

    private unsafe bool TryGetSharedView<T>(ReadOnlySpan<T> span, out bool hasShared, out Memory<T> shared)
    {
        if (_viewCopies is null || span.IsEmpty || RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            hasShared = false;
            shared = default;
            return false;
        }

        fixed (byte* ptr = &Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)))
        {
            var key = ((IntPtr)ptr, (nint)span.Length * Unsafe.SizeOf<T>(), typeof(T));
            if (_viewCopies.TryGetValue(key, out var entry))
            {
                hasShared = entry is Memory<T>;
                shared = entry is Memory<T> memory ? memory : default;
                return true;
            }
        }

        hasShared = false;
        shared = default;
        return false;
    }
}
