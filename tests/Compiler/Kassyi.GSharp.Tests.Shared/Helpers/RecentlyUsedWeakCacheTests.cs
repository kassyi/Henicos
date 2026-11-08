#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

[Category(TestCategory.Unit)]
public class RecentlyUsedWeakCacheTests
{
    private const int Capacity = 4;

    private sealed class Value(int key)
    {
        public int Key { get; } = key;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetOrAdd_SameKey_ReturnsTheValueCreatedOnce()
    {
        var cache = new RecentlyUsedWeakCache<int, Value>(Capacity);
        var created = 0;

        var first = cache.GetOrAdd(1, k => Create(k, ref created));
        var second = cache.GetOrAdd(1, k => Create(k, ref created));

        Assert.Same(first, second);
        Assert.Equal(1, created);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetOrAdd_MoreKeysThanCapacity_KeepsOnlyTheCapacity()
    {
        var cache = new RecentlyUsedWeakCache<int, Value>(Capacity);
        var created = 0;

        for (var key = 0; key < Capacity * 3; key++)
        {
            cache.GetOrAdd(key, k => Create(k, ref created));
        }

        Assert.Equal(Capacity, cache.RecentCount);
        Assert.Equal(Capacity * 3, created);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetOrAdd_ValueLeftTheRecentSetAndNotHeld_IsReleased()
    {
        var cache = new RecentlyUsedWeakCache<int, Value>(Capacity);
        var created = 0;
        var weak = AddAndForget(cache, 0, ref created);
        for (var key = 1; key <= Capacity; key++)
        {
            cache.GetOrAdd(key, k => Create(k, ref created));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.TryGetTarget(out _));
        cache.GetOrAdd(0, k => Create(k, ref created));
        Assert.Equal(Capacity + 2, created);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetOrAdd_ValueLeftTheRecentSetButHeld_ReturnsTheSameValue()
    {
        var cache = new RecentlyUsedWeakCache<int, Value>(Capacity);
        var created = 0;
        var held = cache.GetOrAdd(0, k => Create(k, ref created));
        for (var key = 1; key <= Capacity; key++)
        {
            cache.GetOrAdd(key, k => Create(k, ref created));
        }

        GC.Collect();

        Assert.Same(held, cache.GetOrAdd(0, k => Create(k, ref created)));
        Assert.Equal(Capacity + 1, created);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetOrAdd_RecentValueNotHeld_IsKept()
    {
        var cache = new RecentlyUsedWeakCache<int, Value>(Capacity);
        var created = 0;
        var weak = AddAndForget(cache, 0, ref created);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(weak.TryGetTarget(out var kept));
        Assert.Same(kept, cache.GetOrAdd(0, k => Create(k, ref created)));
        Assert.Equal(1, created);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_ZeroCapacity_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(static () => new RecentlyUsedWeakCache<int, Value>(0));

    private static Value Create(int key, ref int created)
    {
        created++;
        return new Value(key);
    }

    // A separate frame, so that no local of the test keeps the value alive (Debug builds extend local lifetimes)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Value> AddAndForget(RecentlyUsedWeakCache<int, Value> cache, int key, ref int created)
    {
        var count = created;
        var value = cache.GetOrAdd(key, k => Create(k, ref count));
        created = count;
        return new WeakReference<Value>(value);
    }
}
