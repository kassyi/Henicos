using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqFilteringTests
{
    [Fact]
    public void Array_Where_Select_Count_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        int expected = data.Where(x => x > 5)
            .Select(x => x * 2)
            .Count();

        int actual = data.AsStackEnumerable()
            .Where(x => x > 5)
            .Select(x => x * 2)
            .Count();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Span_Where_Select_Count_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Span<int> span = data;

        // Since standard LINQ does not support Span<T>, execute it against the original array (data) to get the expected value.
        int expected = data.Where(x => x % 2 == 0)
            .Select(x => x + 1)
            .Count();

        int actual = span.AsStackEnumerable()
            .Where(x => x % 2 == 0)
            .Select(x => x + 1)
            .Count();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void List_AsStackEnumerable_ShouldMatchStandardLinq()
    {
        var data = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        int expected = data.Where(x => x > 5)
            .Select(x => x * 2)
            .Count();

        int actual = data.AsStackEnumerable()
            .Where(x => x > 5)
            .Select(x => x * 2)
            .Count();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Indexed_Where_Select_ShouldMatchStandardLinq()
    {
        int[] data = { 10, 20, 30, 40, 50, 60 };

        int expected = data.Where((x, i) => x > 20 && i % 2 == 0)
            .Select((x, i) => x + i)
            .Sum();

        int actual = data.AsStackEnumerable()
            .Where((x, i) => x > 20 && i % 2 == 0)
            .Select((x, i) => x + i)
            .Sum();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Count_Predicate_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        int expected = data.Count(x => x % 2 == 0);
        int actual = data.AsStackEnumerable().Count(x => x % 2 == 0);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Count_IndexedPredicate_ShouldMatchStandardLinq()
    {
        int[] data = { 10, 20, 30, 40, 50, 60 };

        int expected = data.Where((x, i) => x > 20 && i % 2 == 0).Count(); // standard LINQ lacks Count((x, i) => ...)
        // x: 10, 20, 30, 40, 50, 60
        // i:  0,  1,  2,  3,  4,  5
        // >20: F, F, T, T, T, T
        // i%2: T, F, T, F, T, F
        // match: 30(2), 50(4) => count is 2

        int actual = data.AsStackEnumerable().Count((x, i) => x > 20 && i % 2 == 0);

        Assert.Equal(2, actual);
    }
}
