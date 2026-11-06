using System.Linq;

namespace Kassyi.ZeroAlloc.Linq.Tests;

[Category(TestCategory.Unit)]
public class StackLinqSumTests
{
    // Each group of 4 adds 1e8 + 1 - 1e8 + 1: 2 per group in a double accumulator, 1 in a float one. Long enough for
    // SIMD on any vector width
    private static readonly float[] s_cancellingFloats =
    [
        1e8f, 1f, -1e8f, 1f, 1e8f, 1f, -1e8f, 1f,
        1e8f, 1f, -1e8f, 1f, 1e8f, 1f, -1e8f, 1f,
    ];

    // In sequence, 1e16 + 1 rounds back to 1e16, so only the last 1 of the array survives the cancellation
    private static readonly double[] s_cancellingDoubles =
    [
        1e16, 1.0, -1e16, 1.0, 1e16, 1.0, -1e16, 1.0,
        1e16, 1.0, -1e16, 1.0, 1e16, 1.0, -1e16, 1.0,
    ];

    [Fact]
    public void SumSequential_FloatAndDouble_MatchesSystemLinq()
    {
        Assert.Equal(8.0f, s_cancellingFloats.Sum());
        Assert.Equal(1.0, s_cancellingDoubles.Sum());

        Assert.Equal(s_cancellingFloats.Sum(), s_cancellingFloats.AsStackEnumerable().SumSequential());
        Assert.Equal(s_cancellingFloats.Sum(), s_cancellingFloats.AsStackEnumerable().SumSequential(static x => x * 1.0f));
        Assert.Equal(s_cancellingDoubles.Sum(), s_cancellingDoubles.AsStackEnumerable().SumSequential());
        Assert.Equal(s_cancellingDoubles.Sum(), s_cancellingDoubles.AsStackEnumerable().SumSequential(static x => x * 1.0));
    }

    // System.Linq adds float in a double accumulator and rounds once at the end
    public static TheoryData<float[]> FloatSums =>
    [
        [16777216f, 1f, 1f],
        [float.MaxValue, float.MaxValue, -float.MaxValue],
        s_cancellingFloats,
    ];

    [Theory]
    [MemberData(nameof(FloatSums))]
    public void Sum_Float_MatchesSystemLinqOnEverySource(float[] data)
    {
        var expected = data.Sum();

        Assert.Equal(expected, data.AsStackEnumerable().Sum());
        Assert.Equal(expected, new ReadOnlySpan<float>(data).AsStackEnumerable().Sum());
        Assert.Equal(expected, data.AsStackEnumerable().Select(static x => x).Sum());
        Assert.Equal(expected, data.AsStackEnumerable().Sum(static x => x));
        Assert.Equal(expected, data.AsStackEnumerable().AsParallel().Sum());
    }

    [Fact]
    public void Sum_Float_IssueExamples()
    {
        float[] carries = [16777216f, 1f, 1f];
        float[] overflowsInFloat = [float.MaxValue, float.MaxValue, -float.MaxValue];

        Assert.Equal(16777218f, carries.AsStackEnumerable().Sum());
        Assert.Equal(float.MaxValue, overflowsInFloat.AsStackEnumerable().Sum());
    }

    // System.Linq adds double in sequence order; a SIMD sum gives a result that depends on the vector width
    public static TheoryData<double[]> DoubleSums =>
    [
        [1e16, 0.5, 0.5, 0.5, -1e16, 0.5, 0.5, 0.5],
        s_cancellingDoubles,
    ];

    [Theory]
    [MemberData(nameof(DoubleSums))]
    public void Sum_Double_MatchesSystemLinqOnEverySource(double[] data)
    {
        var expected = data.Sum();

        Assert.Equal(expected, data.AsStackEnumerable().Sum());
        Assert.Equal(expected, new ReadOnlySpan<double>(data).AsStackEnumerable().Sum());
        Assert.Equal(expected, data.AsStackEnumerable().Select(static x => x).Sum());
        Assert.Equal(expected, data.AsStackEnumerable().Sum(static x => x));
        Assert.Equal(expected, data.AsStackEnumerable().AsParallel().Sum());
    }

    [Fact]
    public void Sum_Double_IssueExample()
    {
        double[] data = [1e16, 0.5, 0.5, 0.5, -1e16, 0.5, 0.5, 0.5];

        Assert.Equal(1.5, data.AsStackEnumerable().Sum());
    }
}
