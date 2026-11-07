using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqPredicatesTests
{
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
    public void Contains_ShouldReturnCorrectResult()
    {
        int[] data = { 1, 2, 3, 4, 5 };

        Assert.True(data.AsStackEnumerable().Contains(3));
        Assert.False(data.AsStackEnumerable().Contains(6));
    }
}
