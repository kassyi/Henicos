using System.Linq;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqAggregationTests
{
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
    public void MaxMinAverage_IntSequence_ReturnsExtremaAndAverage()
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
    public void MaxMinAverage_FloatSequence_ReturnsExtremaAndAverage()
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
    public void MaxMinAverage_DoubleSequence_ReturnsExtremaAndAverage()
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
    public void Max_Min_AllNegativeSequence_ShouldReturnCorrectResult()
    {
        int[] negInts = { -10, -5, -20 };
        Assert.Equal(-5, negInts.AsStackEnumerable().Max());
        Assert.Equal(-20, negInts.AsStackEnumerable().Min());

        float[] negFloats = { -10.5f, -5.25f, -20.75f };
        Assert.Equal(-5.25f, negFloats.AsStackEnumerable().Max());
        Assert.Equal(-20.75f, negFloats.AsStackEnumerable().Min());

        double[] negDoubles = { -10.5, -5.25, -20.75 };
        Assert.Equal(-5.25, negDoubles.AsStackEnumerable().Max());
        Assert.Equal(-20.75, negDoubles.AsStackEnumerable().Min());
    }

    // System.Linq: Max skips NaN and gives NaN only when every element is NaN; Min gives NaN when any element is NaN.
    // Neither depends on where the NaN is
    public static TheoryData<double[]> NaNPlacements =>
    [
        [double.NaN, 1.0],
        [1.0, double.NaN],
        [double.NaN],
        [double.NaN, double.NaN, double.NaN],
        [double.NaN, 2.0, -1.0, double.NaN],
        [2.0, double.NaN, -1.0],
        [-1.0, 2.0, 0.5],
    ];

    [Theory]
    [MemberData(nameof(NaNPlacements))]
    public void MaxMin_DoubleWithNaNAnywhere_MatchesSystemLinq(double[] data)
    {
        Assert.Equal(data.Max(), data.AsStackEnumerable().Max());
        Assert.Equal(data.Min(), data.AsStackEnumerable().Min());
        Assert.Equal(data.Max(), data.AsStackEnumerable().Select(static x => x).Max());
        Assert.Equal(data.Min(), data.AsStackEnumerable().Select(static x => x).Min());
    }

    [Theory]
    [MemberData(nameof(NaNPlacements))]
    public void MaxMin_FloatWithNaNAnywhere_MatchesSystemLinq(double[] values)
    {
        var data = Array.ConvertAll(values, static x => (float)x);

        Assert.Equal(data.Max(), data.AsStackEnumerable().Max());
        Assert.Equal(data.Min(), data.AsStackEnumerable().Min());
        Assert.Equal(data.Max(), data.AsStackEnumerable().Select(static x => x).Max());
        Assert.Equal(data.Min(), data.AsStackEnumerable().Select(static x => x).Min());
    }

    // System.Linq stops at the first NaN of Min: the elements after it are not read
    [Fact]
    public void Min_Double_StopsReadingAtTheFirstNaN()
    {
        double[] data = [3.0, double.NaN, 1.0, 2.0];
        var stackLinqReads = 0;
        var systemLinqReads = 0;

        var stackLinqMin = data.AsStackEnumerable().Select(x => { stackLinqReads++; return x; }).Min();
        var systemLinqMin = data.Select(x => { systemLinqReads++; return x; }).Min();

        Assert.True(double.IsNaN(stackLinqMin));
        Assert.True(double.IsNaN(systemLinqMin));
        Assert.Equal(2, systemLinqReads);
        Assert.Equal(systemLinqReads, stackLinqReads);
    }
}
