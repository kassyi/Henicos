using System;
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
}
