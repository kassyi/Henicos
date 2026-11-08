#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// A cache that keeps its most recently used values and, beyond them, only the values that something else still holds.
/// </summary>
/// <remarks>
/// A value that left the recently used set is returned again while any caller holds it, so callers that compare
/// instances see one value per key; once nothing holds it, the garbage collector may release it and the next lookup
/// creates it again.
/// </remarks>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class RecentlyUsedWeakCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, WeakReference<TValue>> _values;
    private readonly LinkedList<(TKey Key, TValue Value)> _recent = new();
    private readonly Lock _lock = new();

    /// <summary>Creates a cache that keeps the <paramref name="capacity"/> most recently used values.</summary>
    public RecentlyUsedWeakCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _values = new Dictionary<TKey, WeakReference<TValue>>(comparer);
    }

    /// <summary>Gets the number of values the cache keeps alive itself.</summary>
    public int RecentCount
    {
        get
        {
            lock (_lock)
            {
                return _recent.Count;
            }
        }
    }

    /// <summary>
    /// Returns the value of <paramref name="key"/>, created by <paramref name="factory"/> when the cache has none that
    /// is still alive. The factory runs under the cache's lock, so it is called once per key and must be short.
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        lock (_lock)
        {
            if (!_values.TryGetValue(key, out var weak) || !weak.TryGetTarget(out var value))
            {
                value = factory(key);
                _values[key] = new WeakReference<TValue>(value);
                RemoveDeadEntries();
            }

            Touch(key, value);
            return value;
        }
    }

    private void Touch(TKey key, TValue value)
    {
        for (var node = _recent.First; node is not null; node = node.Next)
        {
            if (ReferenceEquals(node.Value.Value, value))
            {
                _recent.Remove(node);
                break;
            }
        }

        _recent.AddFirst((key, value));
        if (_recent.Count > _capacity)
        {
            _recent.RemoveLast();
        }
    }

    // Runs when a value is created, so the dictionary stays as large as the set of keys whose values are alive
    private void RemoveDeadEntries()
    {
        List<TKey>? dead = null;
        foreach (var (key, weak) in _values)
        {
            if (!weak.TryGetTarget(out _))
            {
                (dead ??= []).Add(key);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var key in dead)
        {
            _values.Remove(key);
        }
    }
}
