using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The copies that one CPU launch gives a kernel instead of the caller's arrays, one copy per host array: the
/// <c>[In]</c> array arguments and the arrays that the elements of an <c>[In]</c> or read-only container hold.
/// </summary>
/// <remarks>
/// In C# two holders of one array are one object, and on CUDA they share one device region. So every holder of an
/// array gets the same copy (#985), and an array that a writable argument holds is not copied at all
/// (<see cref="Keep"/>): the kernel writes it as the device writes the region that the writable holder copies back.
/// The CPU launcher creates the table only when a container's elements hold arrays.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HostArrayCopies
{
    private readonly Dictionary<object, object> _copies = new(ReferenceEqualityComparer.Instance);

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
}
