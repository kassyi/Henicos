using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqAllocationAndGuardTests
{
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
    public void CopyTo_ExactAndSmallerDestinations_WritesUpToCapacity()
    {
        int[] data = { 10, 20, 30, 40 };

        // Exact size destination
        Span<int> exactDest = stackalloc int[4];
        int exactWritten = data.AsStackEnumerable().CopyTo(exactDest);
        Assert.Equal(4, exactWritten);
        Assert.Equal(data, exactDest.ToArray());

        // Smaller destination (stops cleanly when destination is full)
        Span<int> smallDest = stackalloc int[2];
        int smallWritten = data.AsStackEnumerable().CopyTo(smallDest);
        Assert.Equal(2, smallWritten);
        Assert.Equal(new[] { 10, 20 }, smallDest.ToArray());

        // Empty destination
        int emptyWritten = data.AsStackEnumerable().CopyTo(Span<int>.Empty);
        Assert.Equal(0, emptyWritten);
    }

    [Fact]
    public void AsStackEnumerable_NullSource_ThrowsArgumentNullException()
    {
        // [WHY] Intentionally pass null to verify ArgumentNullException guard on source collections.
        int[] nullArray = null!;
        List<int> nullList = null!;

        Assert.Throws<ArgumentNullException>(() => nullArray.AsStackEnumerable());
        Assert.Throws<ArgumentNullException>(() => nullList.AsStackEnumerable());
    }

    [Fact]
    public void Where_NullPredicate_ThrowsArgumentNullException()
    {
        int[] data = { 1, 2, 3 };
        AssertThrowsArgNull(() => data.AsStackEnumerable().Where((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Where((Func<int, int, bool>)null!));
    }

    [Fact]
    public void Select_NullSelector_ThrowsArgumentNullException()
    {
        int[] data = { 1, 2, 3 };
        AssertThrowsArgNull(() => data.AsStackEnumerable().Select((Func<int, int>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Select((Func<int, int, int>)null!));
    }

    [Fact]
    public void TakeWhile_SkipWhile_NullPredicate_ThrowsArgumentNullException()
    {
        int[] data = { 1, 2, 3 };
        AssertThrowsArgNull(() => data.AsStackEnumerable().TakeWhile((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().TakeWhile((Func<int, int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().SkipWhile((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().SkipWhile((Func<int, int, bool>)null!));
    }

    [Fact]
    public void Aggregate_Count_NullDelegate_ThrowsArgumentNullException()
    {
        int[] data = { 1, 2, 3 };
        AssertThrowsArgNull(() => data.AsStackEnumerable().Aggregate(0, null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Count((Func<int, bool>)null!));
        AssertThrowsArgNull(() => data.AsStackEnumerable().Count((Func<int, int, bool>)null!));
    }

    [Fact]
    public void PredicatesAndElements_NullDelegate_ThrowsArgumentNullException()
    {
        int[] data = { 1, 2, 3 };
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
