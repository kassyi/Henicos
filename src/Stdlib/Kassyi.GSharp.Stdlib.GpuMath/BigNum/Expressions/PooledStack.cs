namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// A stack in an array rented from ArrayPool, grown by doubling: the walks of an expression tree keep their pending
// nodes and values here instead of on the call stack
internal struct PooledStack<T>()
{
    private T[] _items = ArrayPool<T>.Shared.Rent(16);

    public int Count { get; private set; }

    public ref T Peek() => ref _items[Count - 1];

    public void Push(T item)
    {
        if (Count == _items.Length)
        {
            var grown = ArrayPool<T>.Shared.Rent(_items.Length * 2);
            Array.Copy(_items, grown, Count);
            ArrayPool<T>.Shared.Return(_items, clearArray: true);
            _items = grown;
        }

        _items[Count++] = item;
    }

    public T Pop()
    {
        var item = _items[--Count];
        Array.Clear(_items, Count, 1); // T may hold references
        return item;
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_items, clearArray: true);
        _items = [];
        Count = 0;
    }
}
