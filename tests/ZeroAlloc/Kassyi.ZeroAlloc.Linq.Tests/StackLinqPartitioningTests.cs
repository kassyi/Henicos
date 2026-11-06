using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqPartitioningTests
{
    [Fact]
    public void Take_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        Assert.Equal(data.Take(3).ToArray(), data.AsStackEnumerable().Take(3).ToArray());
        Assert.Equal(data.Take(0).ToArray(), data.AsStackEnumerable().Take(0).ToArray());
        Assert.Equal(data.Take(-3).ToArray(), data.AsStackEnumerable().Take(-3).ToArray());
        Assert.Equal(data.Take(15).ToArray(), data.AsStackEnumerable().Take(15).ToArray());

        // Chained
        Assert.Equal(
            data.Where(x => x > 4).Take(3).ToArray(),
            data.AsStackEnumerable().Where(x => x > 4).Take(3).ToArray());

        // Empty
        Assert.Empty(Array.Empty<int>().AsStackEnumerable().Take(5).ToArray());
    }

    [Fact]
    public void Skip_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        Assert.Equal(data.Skip(3).ToArray(), data.AsStackEnumerable().Skip(3).ToArray());
        Assert.Equal(data.Skip(0).ToArray(), data.AsStackEnumerable().Skip(0).ToArray());
        Assert.Equal(data.Skip(-3).ToArray(), data.AsStackEnumerable().Skip(-3).ToArray());
        Assert.Equal(data.Skip(15).ToArray(), data.AsStackEnumerable().Skip(15).ToArray());

        // Chained
        Assert.Equal(
            data.Where(x => x > 4).Skip(2).ToArray(),
            data.AsStackEnumerable().Where(x => x > 4).Skip(2).ToArray());

        // Empty
        Assert.Empty(Array.Empty<int>().AsStackEnumerable().Skip(5).ToArray());
    }

    [Fact]
    public void TakeLast_SkipLast_Optimized_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        var expectedTakeLast = data.TakeLast(3).ToArray();
        var actualTakeLast = data.AsStackEnumerable().TakeLast(3).ToArray();
        Assert.Equal(expectedTakeLast, actualTakeLast);

        var expectedSkipLast = data.SkipLast(3).ToArray();
        var actualSkipLast = data.AsStackEnumerable().SkipLast(3).ToArray();
        Assert.Equal(expectedSkipLast, actualSkipLast);

        // Edge cases
        Assert.Empty(data.AsStackEnumerable().TakeLast(0).ToArray());
        Assert.Equal(data, data.AsStackEnumerable().TakeLast(15).ToArray());
        Assert.Equal(data, data.AsStackEnumerable().SkipLast(0).ToArray());
        Assert.Empty(data.AsStackEnumerable().SkipLast(15).ToArray());
    }

    [Fact]
    public void TakeWhile_SkipWhile_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        var expectedTake = data.TakeWhile(x => x < 5).ToArray();
        var actualTake = data.AsStackEnumerable().TakeWhile(x => x < 5).ToArray();
        Assert.Equal(expectedTake, actualTake);

        var expectedSkip = data.SkipWhile(x => x < 5).ToArray();
        var actualSkip = data.AsStackEnumerable().SkipWhile(x => x < 5).ToArray();
        Assert.Equal(expectedSkip, actualSkip);
    }

    [Fact]
    public void TakeWhile_SkipWhile_Indexed_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        var expectedTake = data.TakeWhile((x, i) => x < 6 && i < 4).ToArray();
        var actualTake = data.AsStackEnumerable().TakeWhile((x, i) => x < 6 && i < 4).ToArray();
        Assert.Equal(expectedTake, actualTake);

        var expectedSkip = data.SkipWhile((x, i) => x < 6 && i < 4).ToArray();
        var actualSkip = data.AsStackEnumerable().SkipWhile((x, i) => x < 6 && i < 4).ToArray();
        Assert.Equal(expectedSkip, actualSkip);

        // Immediate false
        Assert.Empty(data.AsStackEnumerable().TakeWhile((_, _) => false).ToArray());
        Assert.Equal(data, data.AsStackEnumerable().SkipWhile((_, _) => false).ToArray());

        // All true
        Assert.Equal(data, data.AsStackEnumerable().TakeWhile((_, _) => true).ToArray());
        Assert.Empty(data.AsStackEnumerable().SkipWhile((_, _) => true).ToArray());
    }

    [Fact]
    public void ReadOnlySpan_And_Span_TakeLast_SkipLast_Coverage()
    {
        int[] data = { 10, 20, 30, 40, 50 };
        ReadOnlySpan<int> roSpan = data;
        Span<int> span = data;

        // ReadOnlySpan wrapping
        Assert.Equal(150, roSpan.AsStackEnumerable().Sum());
        Assert.Equal(30, roSpan.AsStackEnumerable().ElementAt(2));

        // Span TakeLast / SkipLast
        Assert.Equal(new[] { 40, 50 }, span.AsStackEnumerable().TakeLast(2).ToArray());
        Assert.Empty(span.AsStackEnumerable().TakeLast(0).ToArray());
        Assert.Equal(data, span.AsStackEnumerable().TakeLast(10).ToArray());

        Assert.Equal(new[] { 10, 20, 30 }, span.AsStackEnumerable().SkipLast(2).ToArray());
        Assert.Equal(data, span.AsStackEnumerable().SkipLast(0).ToArray());
        Assert.Empty(span.AsStackEnumerable().SkipLast(10).ToArray());

        // Span Single & Last optimizations
        Span<int> singleSpan = stackalloc int[] { 42 };
        Assert.Equal(42, singleSpan.AsStackEnumerable().Single());
        Assert.Equal(42, singleSpan.AsStackEnumerable().SingleOrDefault());
        Assert.Equal(42, singleSpan.AsStackEnumerable().Last());
        Assert.Equal(42, singleSpan.AsStackEnumerable().LastOrDefault());

        Span<int> emptySpan = Span<int>.Empty;
        bool singleThrew = false;
        try
        {
            emptySpan.AsStackEnumerable().Single();
        }
        catch (InvalidOperationException)
        {
            singleThrew = true;
        }
        Assert.True(singleThrew);

        Assert.Equal(0, emptySpan.AsStackEnumerable().SingleOrDefault());

        bool lastThrew = false;
        try
        {
            emptySpan.AsStackEnumerable().Last();
        }
        catch (InvalidOperationException)
        {
            lastThrew = true;
        }
        Assert.True(lastThrew);

        Assert.Equal(0, emptySpan.AsStackEnumerable().LastOrDefault());
        Assert.Equal(0, emptySpan.AsStackEnumerable().ElementAtOrDefault(5));
    }
}
