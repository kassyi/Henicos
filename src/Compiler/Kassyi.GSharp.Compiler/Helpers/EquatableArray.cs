using System.Collections;

namespace Kassyi.GSharp.Compiler.Helpers;

/// <summary>
/// A wrapper for ImmutableArray that implements value equality.
/// This is crucial for Roslyn Incremental Generators to properly cache results.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array = array;

    public ImmutableArray<T> AsImmutableArray() => _array;

    public bool IsDefaultOrEmpty => _array.IsDefaultOrEmpty;
    public int Length => _array.IsDefault ? 0 : _array.Length;
    public T this[int index] => _array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault && other._array.IsDefault)
        {
            return true;
        }

        if (_array.IsDefault || other._array.IsDefault)
        {
            return false;
        }

        if (_array.Length != other._array.Length)
        {
            return false;
        }

        for (var i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefault)
        {
            return 0;
        }

        var hash = 17;
        foreach (var item in _array)
        {
            hash = hash * 31 + (item == null ? 0 : item.GetHashCode());
        }

        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public IEnumerator<T> GetEnumerator() => _array.IsDefault
        ? Enumerable.Empty<T>().GetEnumerator()
        : ((IEnumerable<T>)_array).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> AsEquatableArray<T>(this ImmutableArray<T> array) where T : IEquatable<T> =>
        new(array);
}
