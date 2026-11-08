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
        Assert.Equal(
            s_cancellingFloats.Sum(),
            s_cancellingFloats.AsStackEnumerable().SumSequential(static x => x * 1.0f)
        );
        Assert.Equal(s_cancellingDoubles.Sum(), s_cancellingDoubles.AsStackEnumerable().SumSequential());
        Assert.Equal(
            s_cancellingDoubles.Sum(),
            s_cancellingDoubles.AsStackEnumerable().SumSequential(static x => x * 1.0)
        );
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
    public void Sum_LongerThanVector_AddsWholeVectorsAndLeftovers()
    {
        // 101 elements: several whole vectors at any vector width, plus elements left over
        var intData = new int[101];
        var floatData = new float[101];
        var doubleData = new double[101];
        for (var i = 0; i < intData.Length; i++)
        {
            intData[i] = i + 1;
            floatData[i] = i + 1;
            doubleData[i] = i + 1;
        }

        Assert.Equal(5151, intData.AsStackEnumerable().Sum());
        Assert.Equal(5151f, floatData.AsStackEnumerable().Sum());
        Assert.Equal(5151d, doubleData.AsStackEnumerable().Sum());
        Assert.Equal(5151, new ReadOnlySpan<int>(intData).AsStackEnumerable().Sum());
        Assert.Equal(5151f, new ReadOnlySpan<float>(floatData).AsStackEnumerable().Sum());
        Assert.Equal(5151d, new ReadOnlySpan<double>(doubleData).AsStackEnumerable().Sum());
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

        // Non-multiple lengths (67, 7, 0) to verify SIMD vector loop + scalar remainder tail loop
        foreach (int length in new[] { 67, 7, 0 })
        {
            int[] intTail = Enumerable.Range(1, length).ToArray();
            Span<int> intTailSpan = intTail;
            Assert.Equal(intTail.Sum(), intTail.AsStackEnumerable().Sum());
            Assert.Equal(intTail.Sum(), intTailSpan.AsStackEnumerable().Sum());

            float[] floatTail = Enumerable.Range(1, length).Select(i => (float)i).ToArray();
            Span<float> floatTailSpan = floatTail;
            Assert.Equal(floatTail.Sum(), floatTail.AsStackEnumerable().Sum());
            Assert.Equal(floatTail.Sum(), floatTailSpan.AsStackEnumerable().Sum());

            double[] doubleTail = Enumerable.Range(1, length).Select(i => (double)i).ToArray();
            Span<double> doubleTailSpan = doubleTail;
            Assert.Equal(doubleTail.Sum(), doubleTail.AsStackEnumerable().Sum(), precision: 5);
            Assert.Equal(doubleTail.Sum(), doubleTailSpan.AsStackEnumerable().Sum(), precision: 5);
        }
    }
}
