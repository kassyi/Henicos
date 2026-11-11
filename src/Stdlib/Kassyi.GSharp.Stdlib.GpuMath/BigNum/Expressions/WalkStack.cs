namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The pending frames or values of <see cref="ExpressionWalk"/>: the first <see cref="InlineCapacity"/> items live in
/// the stack frame of the walk, so an expression of ordinary depth rents nothing; a deeper one moves them to an array
/// rented from <see cref="ArrayPool{T}.Shared"/>, grown by doubling.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal ref struct WalkStack<T>
{
    /// <summary>The number of items kept without renting an array.</summary>
    public const int InlineCapacity = 8;

    private InlineItems _inline;
    private T[]? _spilled;

    /// <summary>Gets the number of items on the stack.</summary>
    public int Count { get; private set; }

    /// <summary>Returns a reference to the top item; it stays valid until the next <see cref="Push"/>.</summary>
    /// <returns>The top item.</returns>
    [UnscopedRef]
    public ref T Peek() => ref _spilled is null ? ref _inline[Count - 1] : ref _spilled[Count - 1];

    /// <summary>Pushes an item.</summary>
    /// <param name="item">The item.</param>
    public void Push(T item)
    {
        if (_spilled is null)
        {
            if (Count < InlineCapacity)
            {
                _inline[Count++] = item;
                return;
            }

            _spilled = ArrayPool<T>.Shared.Rent(InlineCapacity * 2);
            ((ReadOnlySpan<T>)_inline).CopyTo(_spilled);
            ((Span<T>)_inline).Clear();
        }
        else if (Count == _spilled.Length)
        {
            var grown = ArrayPool<T>.Shared.Rent(_spilled.Length * 2);
            Array.Copy(_spilled, grown, Count);
            ArrayPool<T>.Shared.Return(_spilled, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _spilled = grown;
        }

        _spilled[Count++] = item;
    }

    /// <summary>Pops the top item.</summary>
    /// <returns>The item.</returns>
    public T Pop()
    {
        Count--;
        return _spilled is null ? _inline[Count] : _spilled[Count];
    }

    /// <summary>Returns the rented array, if any, cleared when it may hold references.</summary>
    public void Dispose()
    {
        if (_spilled is not null)
        {
            ArrayPool<T>.Shared.Return(_spilled, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _spilled = null;
        }

        Count = 0;
    }

    [InlineArray(InlineCapacity)]
    private struct InlineItems
    {
        private T _element0;
    }
}
