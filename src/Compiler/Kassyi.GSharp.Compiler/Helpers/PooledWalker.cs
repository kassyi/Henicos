
namespace Kassyi.GSharp.Compiler.Helpers;

/// <summary>
/// A base class for CSharpSyntaxWalker implementations that uses object pooling to eliminate heap allocations.
/// </summary>
public abstract class PooledWalker<T> : CSharpSyntaxWalker, IDisposable where T : PooledWalker<T>, new()
{
    private static readonly ConcurrentBag<T> s_pool = [];
    private int _isRented;

    /// <summary>
    /// Rents a walker instance from the pool or creates a new one if the pool is empty.
    /// </summary>
    protected static T RentCore()
    {
        if (!s_pool.TryTake(out var walker))
        {
            walker = new T();
        }

        Volatile.Write(ref walker._isRented, 1);
        return walker;
    }

    /// <summary>
    /// Returns the walker instance to the pool.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isRented, 0) != 1)
        {
            return;
        }

        OnReturn();
        s_pool.Add((T)this);
    }

    /// <summary>
    /// Override this method to clear state before the instance is returned to the pool.
    /// </summary>
    protected virtual void OnReturn()
    {
    }
}
