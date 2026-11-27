using System.Linq;
using System.Numerics;

namespace Henicos.Benchmarks.Tests;

[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigIntNttProductBenchmarkTests
{
    private const int ExpectedCaseCount = 15 + 15 + 7 + 15 + 15;

    // Closed-form baseline NTT transform length for each benchmark kernel:
    // - InverseSqrt (marginWords = 2): maxOutWords = 4n + 6, M = RoundUpToPowerOf2(2 * maxOutWords)
    // - Reciprocal (targetBits = 64n + 512): resultWords = (targetBits + 31) / 32, maxOutWords = n + 2 * resultWords
    // - ModPow: maxOutWords = 5n + 16
    // - Multiply / Square (35-bit limbs): coeffs = ceil(2n * 32 / 35), M = max(2, RoundUpToPowerOf2(coeffs))
    private static int ClosedFormTransformLength(NttProductKernel kernel, int operandWords)
    {
        return kernel switch
        {
            NttProductKernel.InverseSqrt =>
                checked((int)BitOperations.RoundUpToPowerOf2((uint)(2 * (4 * operandWords + 6)))),
            NttProductKernel.Reciprocal =>
                checked((int)BitOperations.RoundUpToPowerOf2(
                    (uint)(2 * (operandWords + 2 * ((64 * operandWords + 512 + 31) / 32))))),
            NttProductKernel.ModPow =>
                checked((int)BitOperations.RoundUpToPowerOf2((uint)(2 * (5 * operandWords + 16)))),
            NttProductKernel.Multiply or NttProductKernel.Square =>
                checked((int)Math.Max(2u,
                    BitOperations.RoundUpToPowerOf2((uint)(((ulong)(uint)(2 * operandWords) * 32UL + 34UL) / 35UL)))),
            _ => throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown NTT product kernel.")
        };
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Cases_EveryKernelAndLog2Points_HitsExactTransformLengthBoundary()
    {
        // Pull directly from the benchmark's [ParamsSource] so any drift in the benchmark list fails this test.
        var cases = GpuBigIntNttProductBenchmark.Cases().ToArray();
        Assert.Equal(ExpectedCaseCount, cases.Length);

        foreach (var entry in cases)
        {
            var expectedTransformLength = 1 << entry.Log2Points;
            var operandWords = GpuBigIntNttProductBenchmark.OperandWordsFor(entry.Kernel, entry.Log2Points);

            // Both the closed-form formula and the live launcher must yield exactly 2^k at operandWords.
            // mutant: src/Apps/Henicos.Benchmarks/GpuBigIntNttProductBenchmark.cs:127 `half - 6` -> `half - 4`
            // mutant: CooperativeMegaKernelLauncher.cs:262 `+ 2 * ((targetBits + 31) / 32)` -> `+ 1`
            Assert.Equal(expectedTransformLength, ClosedFormTransformLength(entry.Kernel, operandWords));
            Assert.Equal(
                expectedTransformLength,
                GpuBigIntNttProductBenchmark.LiveTransformLengthFor(entry.Kernel, operandWords));

            // Stepping up by one word must double the transform length to 2^(k + 1), except for Multiply/Square
            // at k = 24 where operandWords sits at the 2^23-word product cap and n + 1 must be rejected.
            if (entry.Kernel is NttProductKernel.Multiply or NttProductKernel.Square
                && entry.Log2Points == GpuBigIntNttProductBenchmark.MaxLog2Points)
            {
                Assert.Equal(GpuBigIntNttProductBenchmark.MaxNtt35OperandWords, operandWords);
                Assert.Throws<ArgumentException>(() =>
                    GpuBigIntNttProductBenchmark.LiveTransformLengthFor(entry.Kernel, operandWords + 1));
            }
            else
            {
                var nextTransformLength = expectedTransformLength * 2;
                Assert.Equal(nextTransformLength, ClosedFormTransformLength(entry.Kernel, operandWords + 1));
                Assert.Equal(nextTransformLength,
                    GpuBigIntNttProductBenchmark.LiveTransformLengthFor(entry.Kernel, operandWords + 1));
            }
        }
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(NttProductKernel.InverseSqrt, 9)]
    [InlineData(NttProductKernel.InverseSqrt, 25)]
    [InlineData(NttProductKernel.Reciprocal, 9)]
    [InlineData(NttProductKernel.Reciprocal, 25)]
    [InlineData(NttProductKernel.ModPow, 9)]
    [InlineData(NttProductKernel.ModPow, 17)]
    [InlineData(NttProductKernel.Multiply, 9)]
    [InlineData(NttProductKernel.Multiply, 25)]
    [InlineData(NttProductKernel.Square, 9)]
    [InlineData(NttProductKernel.Square, 25)]
    public void OperandWordsFor_OutOfRangeLog2Points_ThrowsArgumentOutOfRangeException(
        NttProductKernel kernel,
        int log2Points)
    {
        // Log2Points outside each kernel's supported benchmark range must be rejected.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GpuBigIntNttProductBenchmark.OperandWordsFor(kernel, log2Points));
    }
}
