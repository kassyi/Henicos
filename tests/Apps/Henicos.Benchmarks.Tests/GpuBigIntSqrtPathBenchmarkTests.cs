using System.Linq;
using System.Numerics;

namespace Henicos.Benchmarks.Tests;

[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigIntSqrtPathBenchmarkTests
{
    private const int ExpectedPairCount = 10;
    private const int InitialDoublingTransformLength = 512;

    // Closed-form NTT transform length for a 2r-word operand with its top bit set:
    // GpuBigIntDivRem.SqrtThroughSqrtFloor passes an (r + 1)-word top slice and marginWords = (r + 1) / 2
    // into CooperativeMegaKernelLauncher.InverseSqrtMega, which computes
    // maxOutWords = 4 * (r + 1) + 3 * ((r + 1) / 2) and calls TransformLengthFor(maxOutWords).
    // The width formula is a copy of the one in InverseSqrtMega (CooperativeMegaKernelLauncher.cs), and the
    // benchmark's MegaKernelTransformLength repeats it: this test checks the margin and the rounding of the library
    // and that the listed widths sit on the boundaries; a change of the width formula in InverseSqrtMega is not seen.
    private static int ClosedFormTransformLength(int rootWords)
    {
        var maxOutWords = 4 * (rootWords + 1) + 3 * ((rootWords + 1) / 2);
        return checked((int)BitOperations.RoundUpToPowerOf2((uint)(2 * maxOutWords)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RootWordsValues_EveryBoundaryPair_DoublesMegaKernelTransformLength()
    {
        // Pull directly from the benchmark's [ParamsSource] so any drift in the benchmark list fails this test.
        var rootWords = GpuBigIntSqrtPathBenchmark.RootWordsValues().ToArray();

        // Both the internal boundary list and BenchmarkDotNet's [ParamsSource] must expose the same 20 values.
        Assert.Equal(GpuBigIntSqrtPathBenchmark.BoundaryRootWords, rootWords);
        Assert.Equal(ExpectedPairCount * 2, rootWords.Length);

        for (var pairIndex = 0; pairIndex < ExpectedPairCount; pairIndex++)
        {
            var rLow = rootWords[pairIndex * 2];
            var rHigh = rootWords[pairIndex * 2 + 1];
            var expectedLowTransformLength = InitialDoublingTransformLength << pairIndex;
            var expectedHighTransformLength = expectedLowTransformLength * 2;

            // Every pair sits on adjacent root word counts (r, r + 1).
            // mutant: src/Apps/Henicos.Benchmarks/GpuBigIntSqrtPathBenchmark.cs:51 `5956` -> `5955`
            Assert.Equal(rLow + 1, rHigh);

            // Live GpuMath launcher transform length matches the closed-form formula across r - 1, r, and r + 1.
            var closedBefore = ClosedFormTransformLength(rLow - 1);
            var closedLow = ClosedFormTransformLength(rLow);
            var closedAfter = ClosedFormTransformLength(rLow + 1);
            var liveBefore = GpuBigIntSqrtPathBenchmark.MegaKernelTransformLength(rLow - 1);
            var liveLow = GpuBigIntSqrtPathBenchmark.MegaKernelTransformLength(rLow);
            var liveAfter = GpuBigIntSqrtPathBenchmark.MegaKernelTransformLength(rLow + 1);

            Assert.Equal(closedBefore, liveBefore);
            Assert.Equal(closedLow, liveLow);
            Assert.Equal(closedAfter, liveAfter);

            // rLow - 1 and rLow stay at M = 512 * 2^pairIndex; stepping to rLow + 1 (== rHigh) doubles M.
            Assert.Equal(expectedLowTransformLength, liveBefore);
            Assert.Equal(expectedLowTransformLength, liveLow);
            Assert.Equal(expectedHighTransformLength, liveAfter);
            Assert.NotEqual(liveLow, liveAfter);
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(true, int.MaxValue)]
    [InlineData(false, 0)]
    public void ThresholdWordsFor_MegaKernelFlag_SelectsExpectedThreshold(
        bool megaKernel,
        int expectedThresholdWords)
    {
        // MegaKernel=true forces every root width onto the mega-kernel; false forces precision doubling.
        Assert.Equal(expectedThresholdWords, GpuBigIntSqrtPathBenchmark.ThresholdWordsFor(megaKernel));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(-1)]
    public void MegaKernelTransformLength_NonPositiveRootWords_ThrowsArgumentOutOfRangeException(int rootWords)
    {
        // Non-positive root word counts are outside the valid Sqrt domain and must be rejected.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GpuBigIntSqrtPathBenchmark.MegaKernelTransformLength(rootWords));
    }
}
