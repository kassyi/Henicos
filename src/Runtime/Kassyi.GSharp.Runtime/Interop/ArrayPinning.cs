using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The references that the generated launchers pin to pass an array-like argument: a null array, or a span-like value
/// over no array (<c>default</c>), gives a null reference, and an empty array, or a span-like value over one, gives the
/// non-null address of its (absent) first element, so that the device tells the two apart as C# does.
/// </summary>
/// <remarks>
/// <c>fixed (T* p = array)</c> gives a null pointer for an empty array, and the same for an empty span over one; these
/// give the reference of the array's data instead, as the <c>ArrayMarshaller</c> of .NET's <c>LibraryImport</c> does.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ArrayPinning
{
    /// <summary>The reference to pin for <paramref name="array"/>: null for a null array, its data otherwise.</summary>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public static ref T GetPinnableReference<T>(T[]? array) =>
        ref array is null ? ref Unsafe.NullRef<T>() : ref MemoryMarshal.GetArrayDataReference(array);

    /// <summary>The reference to pin for <paramref name="span"/>: null for a span over no array.</summary>
    /// <param name="span">The span.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public static ref T GetPinnableReference<T>(Span<T> span) => ref MemoryMarshal.GetReference(span);

    /// <summary>The reference to pin for <paramref name="span"/>: null for a span over no array.</summary>
    /// <param name="span">The span.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public static ref T GetPinnableReference<T>(ReadOnlySpan<T> span) => ref MemoryMarshal.GetReference(span);

    /// <summary>Whether <paramref name="array"/> is null.</summary>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public static bool IsNull<T>(T[]? array) => array is null;

    /// <summary>Whether <paramref name="span"/> is over no array (<c>default</c>, which C# compares equal to null).</summary>
    /// <param name="span">The span.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public static bool IsNull<T>(Span<T> span) => Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span));

    /// <summary>Whether <paramref name="span"/> is over no array (<c>default</c>, which C# compares equal to null).</summary>
    /// <param name="span">The span.</param>
    /// <typeparam name="T">The element type.</typeparam>
    public static bool IsNull<T>(ReadOnlySpan<T> span) => Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span));
}
