using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqElementsTests
{
    [Fact]
    public void First_ShouldReturnFirstMatchingElement()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        Assert.Equal(1, data.AsStackEnumerable().First());
        Assert.Equal(4, data.AsStackEnumerable().First(x => x > 3));

        Assert.Throws<InvalidOperationException>(() => Array.Empty<int>().AsStackEnumerable().First());
        Assert.Throws<InvalidOperationException>(() => data.AsStackEnumerable().First(x => x > 10));
    }

    [Fact]
    public void FirstOrDefault_ShouldReturnFirstMatchingElementOrDefault()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        Assert.Equal(1, data.AsStackEnumerable().FirstOrDefault());
        Assert.Equal(4, data.AsStackEnumerable().FirstOrDefault(x => x > 3));

        Assert.Equal(0, Array.Empty<int>().AsStackEnumerable().FirstOrDefault());
        Assert.Equal(0, data.AsStackEnumerable().FirstOrDefault(x => x > 10));
    }

    [Fact]
    public void Single_ShouldReturnCorrectResult()
    {
        int[] data1 = { 10 };
        int[] data2 = { 10, 20 };
        int[] data3 = { 10, 20, 30 };

        Assert.Equal(10, data1.AsStackEnumerable().Single());
        Assert.Equal(10, data1.AsStackEnumerable().SingleOrDefault());
        Assert.Throws<InvalidOperationException>(() => data2.AsStackEnumerable().Single());

        Assert.Equal(20, data3.AsStackEnumerable().Single(x => x == 20));
        Assert.Equal(0, data3.AsStackEnumerable().SingleOrDefault(x => x == 40));
        Assert.Throws<InvalidOperationException>(() => data3.AsStackEnumerable().Single(x => x > 10)); // matches 20 and 30
    }

    [Fact]
    public void Last_ShouldReturnCorrectResult()
    {
        int[] data = { 10, 20, 30 };

        // Optimized SpanEnumerator execution
        Assert.Equal(30, data.AsStackEnumerable().Last());
        Assert.Equal(30, data.AsStackEnumerable().LastOrDefault());

        // Empty source
        int[] empty = Array.Empty<int>();
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Last());
        Assert.Equal(0, empty.AsStackEnumerable().LastOrDefault());

        // Chained execution (O(N))
        Assert.Equal(20, data.AsStackEnumerable().Where(x => x < 30).Last());
        Assert.Equal(20, data.AsStackEnumerable().Where(x => x < 30).LastOrDefault());
    }

    [Fact]
    public void ElementAt_ShouldReturnCorrectResult()
    {
        int[] data = { 10, 20, 30, 40, 50 };

        // Test SpanEnumerator directly
        Assert.Equal(30, data.AsStackEnumerable().ElementAt(2));
        Assert.Equal(0, data.AsStackEnumerable().ElementAtOrDefault(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.AsStackEnumerable().ElementAt(10));

        // Test WhereEnumerator chaining
        Assert.Equal(40, data.AsStackEnumerable().Where(x => x > 20).ElementAt(1));
        Assert.Equal(0, data.AsStackEnumerable().Where(x => x > 20).ElementAtOrDefault(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.AsStackEnumerable().Where(x => x > 20).ElementAt(10));
    }

    [Fact]
    public void NegativeIndexAndCount_Boundaries_ShouldMatchStandardLinq()
    {
        int[] data = { 10, 20, 30, 40, 50 };
        Span<int> span = data;

        // ElementAt(-1) throws ArgumentOutOfRangeException on Array, Span, and Chained enumerators
        Assert.Throws<ArgumentOutOfRangeException>(() => data.AsStackEnumerable().ElementAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.AsStackEnumerable().Where(x => x > 10).ElementAt(-1));

        bool spanElementAtThrew = false;
        try
        {
            span.AsStackEnumerable().ElementAt(-1);
        }
        catch (ArgumentOutOfRangeException)
        {
            spanElementAtThrew = true;
        }
        Assert.True(spanElementAtThrew);

        // ElementAtOrDefault(-1) returns default (0)
        Assert.Equal(0, data.AsStackEnumerable().ElementAtOrDefault(-1));
        Assert.Equal(0, span.AsStackEnumerable().ElementAtOrDefault(-1));
        Assert.Equal(0, data.AsStackEnumerable().Where(x => x > 10).ElementAtOrDefault(-1));

        // TakeLast(-1) returns empty
        Assert.Empty(data.AsStackEnumerable().TakeLast(-1).ToArray());
        Assert.Empty(span.AsStackEnumerable().TakeLast(-1).ToArray());

        // SkipLast(-1) returns full sequence
        Assert.Equal(data, data.AsStackEnumerable().SkipLast(-1).ToArray());
        Assert.Equal(data, span.AsStackEnumerable().SkipLast(-1).ToArray());
    }
}
