using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The byte references that a launcher pins to compare its arguments for overlapping memory
/// (<see cref="HostAliasGroups"/>): null for a null array or a span over none.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HostAliasPins
{
    /// <summary>The reference to pin for <paramref name="array"/>: null for a null array.</summary>
    public static ref byte Start<T>(T[]? array) =>
        ref array is null
            ? ref Unsafe.NullRef<byte>()
            : ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(array));

    /// <summary>The reference to pin for a multidimensional <paramref name="array"/>: null for a null array.</summary>
    public static ref byte Start(Array? array) =>
        ref array is null ? ref Unsafe.NullRef<byte>() : ref MemoryMarshal.GetArrayDataReference(array);

    /// <summary>The reference to pin for <paramref name="span"/>: null for a span over no array.</summary>
    public static ref byte Start<T>(Span<T> span) => ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span));

    /// <summary>The reference to pin for <paramref name="span"/>: null for a span over no array.</summary>
    public static ref byte Start<T>(ReadOnlySpan<T> span) =>
        ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span));

    /// <summary>The reference to pin for a <c>ref</c> or <c>out</c> argument.</summary>
    public static ref byte StartOf<T>(ref T value) => ref Unsafe.As<T, byte>(ref value);
}
