using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

/// <summary>
/// Overloads that pass a state value to a <see langword="static"/> lambda, so that a query using an outer value does not
/// allocate a closure.
/// </summary>
public class StackLinqStateTests
{
    private static readonly int[] s_data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Fact]
    public void Where_Select_Count_WithState_ShouldMatchStandardLinq()
    {
        var threshold = 5;
        var factor = 3;

        int expected = s_data.Where(x => x > threshold)
            .Select(x => x * factor)
            .Count(x => x % 2 == 0);

        int actual = s_data.AsStackEnumerable()
            .Where(threshold, static (x, t) => x > t)
            .Select(factor, static (x, f) => x * f)
            .Count(2, static (x, d) => x % d == 0);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Any_All_WithState_ShouldUseTheState()
    {
        Assert.True(s_data.AsStackEnumerable().Any(10, static (x, v) => x == v));
        Assert.False(s_data.AsStackEnumerable().Any(11, static (x, v) => x == v));
        Assert.True(s_data.AsStackEnumerable().All(0, static (x, v) => x > v));
        Assert.False(s_data.AsStackEnumerable().All(1, static (x, v) => x > v));
    }

    [Fact]
    public void First_FirstOrDefault_WithState_ShouldReturnTheFirstMatch()
    {
        Assert.Equal(4, s_data.AsStackEnumerable().First(3, static (x, v) => x > v));
        Assert.Equal(4, s_data.AsStackEnumerable().FirstOrDefault(3, static (x, v) => x > v));
        Assert.Equal(0, s_data.AsStackEnumerable().FirstOrDefault(10, static (x, v) => x > v));
    }

    [Fact]
    public void WithState_OnEmptySource_ShouldReturnTheEmptyResults()
    {
        int[] empty = [];

        Assert.False(empty.AsStackEnumerable().Any(0, static (x, v) => x == v));
        Assert.True(empty.AsStackEnumerable().All(0, static (x, v) => x == v));
        Assert.Equal(0, empty.AsStackEnumerable().Count(0, static (x, v) => x == v));
        Assert.Equal(0, empty.AsStackEnumerable().FirstOrDefault(0, static (x, v) => x == v));
        Assert.Equal(0, empty.AsStackEnumerable()
            .Where(0, static (x, v) => x == v)
            .Count());
    }

    [Fact]
    public void First_WithState_NoMatch_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = s_data.AsStackEnumerable().First(10, static (x, v) => x > v); });
    }

    [Fact]
    public void WithState_NullFunction_ShouldThrow()
    {
        // Intentionally passing null to assert the ArgumentNullException guards
        Assert.Throws<ArgumentNullException>(() => { _ = s_data.AsStackEnumerable().Where(0, (Func<int, int, bool>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = s_data.AsStackEnumerable().Select(0, (Func<int, int, int>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = s_data.AsStackEnumerable().Count(0, (Func<int, int, bool>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = s_data.AsStackEnumerable().Any(0, (Func<int, int, bool>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = s_data.AsStackEnumerable().All(0, (Func<int, int, bool>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = s_data.AsStackEnumerable().First(0, (Func<int, int, bool>)null!); });
        Assert.Throws<ArgumentNullException>(
            () => { _ = s_data.AsStackEnumerable().FirstOrDefault(0, (Func<int, int, bool>)null!); }
        );
    }

    [Fact]
    public void WithState_StaticLambdas_ShouldBeZeroAllocation()
    {
        RunQuery("ccc");

        long before = GC.GetAllocatedBytesForCurrentThread();
        int actual = RunQuery("ccc");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(3, actual);
        Assert.Equal(0, allocated);
    }

    private static readonly string[] s_names = ["a", "bb", "ccc", "dddd"];

    private static int RunQuery(string target) =>
        s_names.AsStackEnumerable()
            .Where(target, static (n, t) => n.Length <= t.Length)
            .Select(target, static (n, t) => n == t ? n.Length : 0)
            .Sum(static n => n);
}
