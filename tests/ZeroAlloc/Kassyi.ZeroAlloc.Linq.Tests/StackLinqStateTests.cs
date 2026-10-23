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
    private static readonly int[] s_empty = [];
    private static readonly string[] s_names = ["a", "bb", "ccc", "dddd"];

    [Fact]
    public void Where_Select_Count_WithState_ShouldMatchStandardLinq()
    {
        var threshold = 5;
        var factor = 3;

        int expected = s_data
            .Where(x => x > threshold)
            .Select(x => x * factor)
            .Count(x => x % 2 == 0);

        int actual = s_data
            .AsStackEnumerable()
            .Where(threshold, static (x, t) => x > t)
            .Select(factor, static (x, f) => x * f)
            .Count(2, static (x, d) => x % d == 0);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Any_WithState_ShouldUseTheState(int value, bool expected)
    {
        bool actual = s_data
            .AsStackEnumerable()
            .Any(value, static (x, v) => x == v);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void All_WithState_ShouldUseTheState(int lowerBound, bool expected)
    {
        bool actual = s_data
            .AsStackEnumerable()
            .All(lowerBound, static (x, v) => x > v);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void First_FirstOrDefault_WithState_ShouldReturnTheFirstMatch()
    {
        int first = s_data
            .AsStackEnumerable()
            .First(3, static (x, v) => x > v);
        int firstOrDefault = s_data
            .AsStackEnumerable()
            .FirstOrDefault(3, static (x, v) => x > v);
        int noMatch = s_data
            .AsStackEnumerable()
            .FirstOrDefault(10, static (x, v) => x > v);

        Assert.Equal(4, first);
        Assert.Equal(4, firstOrDefault);
        Assert.Equal(0, noMatch);
    }

    [Fact]
    public void WithState_OnEmptySource_ShouldReturnTheEmptyResults()
    {
        bool any = s_empty
            .AsStackEnumerable()
            .Any(0, static (x, v) => x == v);
        bool all = s_empty
            .AsStackEnumerable()
            .All(0, static (x, v) => x == v);
        int count = s_empty
            .AsStackEnumerable()
            .Count(0, static (x, v) => x == v);
        int firstOrDefault = s_empty
            .AsStackEnumerable()
            .FirstOrDefault(0, static (x, v) => x == v);
        int whereCount = s_empty
            .AsStackEnumerable()
            .Where(0, static (x, v) => x == v)
            .Count();

        Assert.False(any);
        Assert.True(all);
        Assert.Equal(0, count);
        Assert.Equal(0, firstOrDefault);
        Assert.Equal(0, whereCount);
    }

    [Fact]
    public void First_WithState_NoMatch_ShouldThrow()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .First(10, static (x, v) => x > v);
        });
    }

    [Fact]
    public void WithState_NullFunction_ShouldThrow()
    {
        // Intentionally passing null to assert the ArgumentNullException guards
        Func<int, int, bool> predicate = null!;
        Func<int, int, int> selector = null!;

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .Where(0, predicate);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .Select(0, selector);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .Count(0, predicate);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .Any(0, predicate);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .All(0, predicate);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .First(0, predicate);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .FirstOrDefault(0, predicate);
        });
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

    private static int RunQuery(string target) =>
        s_names
            .AsStackEnumerable()
            .Where(target, static (n, t) => n.Length <= t.Length)
            .Select(target, static (n, t) => n == t ? n.Length : 0)
            .Sum(static n => n);
}
