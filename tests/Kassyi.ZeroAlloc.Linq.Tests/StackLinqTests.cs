using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

public class StackLinqTests
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
    public void Execution_ShouldBeZeroAllocation()
    {
        int[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Span<int> span = data;

        // Warm-up
        RunQuery(span);

        // Measurement
        long memoryBefore = GC.GetAllocatedBytesForCurrentThread();
        int actual = RunQuery(span);
        long memoryAfter = GC.GetAllocatedBytesForCurrentThread();

        long allocated = memoryAfter - memoryBefore;

        int expected = data.Where(x => x % 2 != 0)
            .Select(x => x * 3)
            .Count();

        Assert.Equal(expected, actual);
        Assert.Equal(0, allocated);
    }

    private int RunQuery(Span<int> span)
    {
        return span.AsStackEnumerable()
            .Where(x => x % 2 != 0)
            .Select(x => x * 3)
            .Count();
    }

    [Fact]
    public void Any_ShouldReturnTrueIfMatchingElementExists()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        Assert.True(data.AsStackEnumerable().Any());
        Assert.True(data.AsStackEnumerable().Any(x => x == 3));
        Assert.False(data.AsStackEnumerable().Any(x => x == 10));
    }

    [Fact]
    public void All_ShouldReturnTrueIfAllElementsMatch()
    {
        int[] data = { 2, 4, 6, 8 };

        Assert.True(data.AsStackEnumerable().All(x => x % 2 == 0));
        Assert.False(data.AsStackEnumerable().All(x => x > 4));
    }

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
    public void Sum_ShouldReturnSumOfElements()
    {
        int[] intData = { 1, 2, 3, 4, 5 };
        Assert.Equal(15, intData.AsStackEnumerable().Sum());

        float[] floatData = { 1.5f, 2.5f, 3.5f };
        Assert.Equal(7.5f, floatData.AsStackEnumerable().Sum());

        double[] doubleData = { 1.1, 2.2, 3.3 };
        Assert.Equal(6.6, doubleData.AsStackEnumerable().Sum(), precision: 5);
    }

    [Fact]
    public void ToArray_ShouldReturnArrayOfElements()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        int[] expected = data.Where(x => x > 2).ToArray();
        int[] actual = data.AsStackEnumerable().Where(x => x > 2).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CopyTo_ShouldCopyToSpanWithoutAllocation()
    {
        int[] data = { 1, 2, 3, 4, 5, 6 };
        Span<int> buffer = stackalloc int[10];
        Span<int> source = data;

        // Warm-up
        RunCopyToQuery(source, buffer);
        buffer.Clear(); // Clear before actual measurement

        // Measurement
        long memoryBefore = GC.GetAllocatedBytesForCurrentThread();
        int written = RunCopyToQuery(source, buffer);
        long memoryAfter = GC.GetAllocatedBytesForCurrentThread();

        long allocated = memoryAfter - memoryBefore;

        Assert.Equal(4, written);
        Assert.Equal(0, allocated);

        int[] expected = { 30, 40, 50, 60 };
        for (int i = 0; i < written; i++)
        {
            Assert.Equal(expected[i], buffer[i]);
        }
    }

    private int RunCopyToQuery(Span<int> source, Span<int> dest)
    {
        return source.AsStackEnumerable()
            .Where(x => x > 2)
            .Select(x => x * 10)
            .CopyTo(dest);
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
    public void Selector_Sum_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        int expectedInt = data.Sum(x => x * 2);
        int actualInt = data.AsStackEnumerable().Sum(x => x * 2);
        Assert.Equal(expectedInt, actualInt);

        float expectedFloat = data.Sum(x => x * 1.5f);
        float actualFloat = data.AsStackEnumerable().Sum(x => x * 1.5f);
        Assert.Equal(expectedFloat, actualFloat);

        double expectedDouble = data.Sum(x => x * 1.1);
        double actualDouble = data.AsStackEnumerable().Sum(x => x * 1.1);
        Assert.Equal(expectedDouble, actualDouble, precision: 5);
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
    public void Contains_ShouldReturnCorrectResult()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        Assert.True(data.AsStackEnumerable().Contains(3));
        Assert.False(data.AsStackEnumerable().Contains(6));
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
    public void Aggregate_ShouldMatchStandardLinq()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        int expected = data.Aggregate(10, (acc, x) => acc + x);
        int actual = data.AsStackEnumerable().Aggregate(10, (acc, x) => acc + x);
        Assert.Equal(expected, actual);

        // Empty source
        Assert.Equal(42, Array.Empty<int>().AsStackEnumerable().Aggregate(42, (acc, x) => acc + x));
    }

    [Fact]
    public void Max_Min_Average_Int_ShouldReturnCorrectResult()
    {
        int[] data = { 5, 2, 9, 1, 7 };
        Assert.Equal(9, data.AsStackEnumerable().Max());
        Assert.Equal(1, data.AsStackEnumerable().Min());
        Assert.Equal(4.8, data.AsStackEnumerable().Average(), precision: 5);

        // Empty sequence throws
        int[] empty = Array.Empty<int>();
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Max());
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Min());
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Average());
    }

    [Fact]
    public void Max_Min_Average_Float_ShouldReturnCorrectResult()
    {
        float[] data = { 5.5f, 2.2f, 9.9f, 1.1f, 7.7f };
        Assert.Equal(9.9f, data.AsStackEnumerable().Max());
        Assert.Equal(1.1f, data.AsStackEnumerable().Min());
        Assert.Equal(5.28f, data.AsStackEnumerable().Average(), precision: 4);

        // Empty sequence throws
        float[] empty = Array.Empty<float>();
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Max());
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Min());
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Average());
    }

    [Fact]
    public void Max_Min_Average_Double_ShouldReturnCorrectResult()
    {
        double[] data = { 5.5, 2.2, 9.9, 1.1, 7.7 };
        Assert.Equal(9.9, data.AsStackEnumerable().Max());
        Assert.Equal(1.1, data.AsStackEnumerable().Min());
        Assert.Equal(5.28, data.AsStackEnumerable().Average(), precision: 5);

        // Empty sequence throws
        double[] empty = Array.Empty<double>();
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Max());
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Min());
        Assert.Throws<InvalidOperationException>(() => empty.AsStackEnumerable().Average());
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

    [Fact]
    public void Simd_Sum_ShouldMatchStandardLinq()
    {
        // 64 elements to trigger Vector<T> hardware acceleration paths
        int[] intData = Enumerable.Range(1, 64).ToArray();
        Span<int> intSpan = intData;
        int expectedInt = intData.Sum();
        Assert.Equal(expectedInt, intData.AsStackEnumerable().Sum());
        Assert.Equal(expectedInt, intSpan.AsStackEnumerable().Sum());
        Assert.Equal(expectedInt, intData.AsStackEnumerable().Where(_ => true).Sum());

        float[] floatData = Enumerable.Range(1, 64).Select(i => (float)i).ToArray();
        Span<float> floatSpan = floatData;
        float expectedFloat = floatData.Sum();
        Assert.Equal(expectedFloat, floatData.AsStackEnumerable().Sum());
        Assert.Equal(expectedFloat, floatSpan.AsStackEnumerable().Sum());
        Assert.Equal(expectedFloat, floatData.AsStackEnumerable().Where(_ => true).Sum());

        double[] doubleData = Enumerable.Range(1, 64).Select(i => (double)i).ToArray();
        Span<double> doubleSpan = doubleData;
        double expectedDouble = doubleData.Sum();
        Assert.Equal(expectedDouble, doubleData.AsStackEnumerable().Sum());
        Assert.Equal(expectedDouble, doubleSpan.AsStackEnumerable().Sum());
        Assert.Equal(expectedDouble, doubleData.AsStackEnumerable().Where(_ => true).Sum());
    }

    [Fact]
    public void ArgumentNullExceptions_ShouldThrowOnNullInputs()
    {
        int[] nullArray = null!;
        List<int> nullList = null!;
        int[] data = { 1, 2, 3 };

        Assert.Throws<ArgumentNullException>(() => nullArray.AsStackEnumerable());
        Assert.Throws<ArgumentNullException>(() => nullList.AsStackEnumerable());

        AssertThrowsArgNull(() => data.AsStackEnumerable().Where((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Where((Func<int, int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Select((Func<int, int>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Select((Func<int, int, int>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().TakeWhile((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().TakeWhile((Func<int, int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().SkipWhile((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().SkipWhile((Func<int, int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Aggregate(0, null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Count((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Count((Func<int, int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Any(null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().All(null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().First(null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().FirstOrDefault(null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Single(null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().SingleOrDefault(null!));
    }

    private static void AssertThrowsArgNull(Action action)
    {
        Assert.Throws<ArgumentNullException>(action);
    }
}
