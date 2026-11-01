namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqSumTests
{
    [Fact]
    public void SumSequential_FloatAndDouble_AddsInIndexOrderWithoutSimdReassociation()
    {
        // Sequential summation cancels (1e8 + 1) - 1e8 -> 0, + 1 -> 1 on each group of 4, ending at 1.0f / 1.0.
        float[] floatData =
        [
            1e8f, 1f, -1e8f, 1f, 1e8f, 1f, -1e8f, 1f,
            1e8f, 1f, -1e8f, 1f, 1e8f, 1f, -1e8f, 1f
        ];
        double[] doubleData =
        [
            1e16, 1.0, -1e16, 1.0, 1e16, 1.0, -1e16, 1.0,
            1e16, 1.0, -1e16, 1.0, 1e16, 1.0, -1e16, 1.0
        ];

        Assert.Equal(1.0f, floatData.AsStackEnumerable().SumSequential());
        Assert.Equal(1.0f, floatData.AsStackEnumerable().SumSequential(static x => x * 1.0f));
        Assert.Equal(1.0, doubleData.AsStackEnumerable().SumSequential());
        Assert.Equal(1.0, doubleData.AsStackEnumerable().SumSequential(static x => x * 1.0));
    }
}

