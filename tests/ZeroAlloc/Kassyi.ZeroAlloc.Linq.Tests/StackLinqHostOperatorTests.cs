using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

/// <summary>
/// Host-only operators: adding to a caller-owned collection, running an action per element, flattening, and searches
/// that report whether they found anything.
/// </summary>
public class StackLinqHostOperatorTests
{
    private static readonly int[] s_data = [1, 2, 3, 4, 5, 6];
    private static readonly int[] s_empty = [];
    private static readonly int[][] s_nested = [[1, 2], [], [3], [4, 5, 6]];
    private static readonly object[] s_mixed = [1, "a", 2, null, "b"];

    [Fact]
    public void AddTo_ShouldAppendEveryElementAndReturnTheCount()
    {
        var destination = new List<int> { 0 };

        int added = s_data
            .AsStackEnumerable()
            .AddTo(destination);

        Assert.Equal(6, added);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], destination);
    }

    [Fact]
    public void AddIf_ShouldAppendMatchingElements()
    {
        var destination = new List<int>();
        var stateDestination = new List<int>();

        int added = s_data
            .AsStackEnumerable()
            .AddIf(destination, static x => x % 2 == 0);
        int stateAdded = s_data
            .AsStackEnumerable()
            .AddIf(stateDestination, 4, static (x, limit) => x >= limit);

        Assert.Equal(3, added);
        Assert.Equal([2, 4, 6], destination);
        Assert.Equal(3, stateAdded);
        Assert.Equal([4, 5, 6], stateDestination);
    }

    [Fact]
    public void AddIf_WithHashSetState_ShouldKeepTheFirstOfEachValue()
    {
        int[] withDuplicates = [3, 1, 3, 2, 1];
        var distinct = new List<int>();

        withDuplicates
            .AsStackEnumerable()
            .AddIf(distinct, new HashSet<int>(), static (x, seen) => seen.Add(x));

        Assert.Equal([3, 1, 2], distinct);
    }

    [Fact]
    public void Do_DoIf_ShouldRunTheActionPerElement()
    {
        var all = new List<int>();
        var odd = new List<int>();

        s_data
            .AsStackEnumerable()
            .Do(all, static (x, list) => list.Add(x));
        s_data
            .AsStackEnumerable()
            .DoIf(odd, static (x, _) => x % 2 != 0, static (x, list) => list.Add(x * 10));

        Assert.Equal(s_data, all);
        Assert.Equal([10, 30, 50], odd);
    }

    [Fact]
    public void Do_DoIf_WithoutState_ShouldRunTheAction()
    {
        s_counter = 0;

        s_data
            .AsStackEnumerable()
            .Do(static x => s_counter += x);
        s_data
            .AsStackEnumerable()
            .DoIf(static x => x > 4, static x => s_counter += 100);

        Assert.Equal(21 + 200, s_counter);
    }

    [Fact]
    public void SelectMany_ShouldFlattenInOrderAndSkipEmptyInnerSequences()
    {
        int[] expected = s_nested
            .SelectMany(static inner => inner)
            .Where(static x => x > 1)
            .ToArray();

        int[] actual = s_nested
            .AsStackEnumerable()
            .SelectMany(static inner => inner.AsStackEnumerable())
            .Where(static x => x > 1)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SelectMany_OnEmptySource_ShouldBeEmpty()
    {
        int[][] none = [];
        int[][] onlyEmpty = [[], []];

        int fromNone = none
            .AsStackEnumerable()
            .SelectMany(static inner => inner.AsStackEnumerable())
            .Count();
        int fromOnlyEmpty = onlyEmpty
            .AsStackEnumerable()
            .SelectMany(static inner => inner.AsStackEnumerable())
            .Count();

        Assert.Equal(0, fromNone);
        Assert.Equal(0, fromOnlyEmpty);
    }

    [Fact]
    public void TryFirst_ShouldReportWhetherAMatchWasFound()
    {
        bool found = s_data
            .AsStackEnumerable()
            .TryFirst(static x => x > 3, out int value);
        bool stateFound = s_data
            .AsStackEnumerable()
            .TryFirst(5, static (x, limit) => x >= limit, out int stateValue);
        bool notFound = s_data
            .AsStackEnumerable()
            .TryFirst(static x => x > 100, out int missing);

        Assert.True(found);
        Assert.Equal(4, value);
        Assert.True(stateFound);
        Assert.Equal(5, stateValue);
        Assert.False(notFound);
        Assert.Equal(0, missing);
    }

    [Fact]
    public void TryFirst_ShouldTellADefaultMatchFromNoMatch()
    {
        int[] withZero = [0, 1];

        bool foundZero = withZero
            .AsStackEnumerable()
            .TryFirst(static x => x == 0, out int zero);

        Assert.True(foundZero);
        Assert.Equal(0, zero);
    }

    [Fact]
    public void OfType_ShouldKeepTheElementsOfTheTypeInOrder()
    {
        var strings = new List<string>();
        var ints = new List<int>();

        s_mixed
            .AsStackEnumerable()
            .OfType<string>()
            .AddTo(strings);
        s_mixed
            .AsStackEnumerable()
            .OfType<int>()
            .AddTo(ints);

        Assert.Equal(["a", "b"], strings);
        Assert.Equal([1, 2], ints);
    }

    [Fact]
    public void OfType_AfterNullableSelect_ShouldDropTheNulls()
    {
        var hits = new List<string>();

        int added = s_mixed
            .AsStackEnumerable()
            .Select(static x => x as string)
            .OfType<string>()
            .AddTo(hits);
        int fromEmpty = s_empty
            .AsStackEnumerable()
            .OfType<int>()
            .Count();

        Assert.Equal(2, added);
        Assert.Equal(["a", "b"], hits);
        Assert.Equal(0, fromEmpty);
    }

    [Fact]
    public void IndexOf_ShouldReturnThePositionOrMinusOne()
    {
        int index = s_data
            .AsStackEnumerable()
            .IndexOf(static x => x == 4);
        int stateIndex = s_data
            .AsStackEnumerable()
            .IndexOf(6, static (x, target) => x == target);
        int missing = s_data
            .AsStackEnumerable()
            .IndexOf(static x => x == 100);
        int empty = s_empty
            .AsStackEnumerable()
            .IndexOf(static x => x == 1);

        Assert.Equal(3, index);
        Assert.Equal(5, stateIndex);
        Assert.Equal(-1, missing);
        Assert.Equal(-1, empty);
    }

    [Fact]
    public void HostOperators_NullArguments_ShouldThrow()
    {
        // Intentionally passing null to assert the ArgumentNullException guards
        ICollection<int> destination = null!;
        Func<int, bool> predicate = null!;
        Action<int> action = null!;
        Func<int[], StackEnumerable<ArrayEnumerator<int>, int>> selector = null!;

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .AddTo(destination);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .AddIf(new List<int>(), predicate);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            s_data
                .AsStackEnumerable()
                .Do(action);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            s_data
                .AsStackEnumerable()
                .DoIf(static x => true, action);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_nested
                .AsStackEnumerable()
                .SelectMany(selector)
                .Count();
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .TryFirst(predicate, out _);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = s_data
                .AsStackEnumerable()
                .IndexOf(predicate);
        });
    }

    [Fact]
    public void HostOperators_StaticLambdas_ShouldBeZeroAllocation()
    {
        RunQueries();

        long before = GC.GetAllocatedBytesForCurrentThread();
        int actual = RunQueries();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(6 + 3 + 4 + 2, actual);
        Assert.Equal(0, allocated);
    }

    private static int s_counter;

    private static int RunQueries()
    {
        int flattened = s_nested
            .AsStackEnumerable()
            .SelectMany(static inner => inner.AsStackEnumerable())
            .Count();
        s_data
            .AsStackEnumerable()
            .TryFirst(3, static (x, target) => x == target, out int third);
        int index = s_data
            .AsStackEnumerable()
            .IndexOf(5, static (x, target) => x == target);
        int strings = s_mixed
            .AsStackEnumerable()
            .OfType<string>()
            .Count();

        return flattened + third + index + strings;
    }
}
