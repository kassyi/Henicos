using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The approximate inverse square root behind SqrtFloor, on the cooperative mega-kernel.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntApproximateInverseSqrtTests(ITestOutputHelper output)
{
    /// <summary>
    /// Inputs of the approximate inverse square root: the kind of operand and its words <c>n</c>, with the margin
    /// <c>SqrtFloorMarginWords(n - 1)</c> that SqrtFloor uses for a root of <c>n - 1</c> words.
    /// </summary>
    public static TheoryData<string, int> ApproximateInverseSqrtInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "Random", "PowerOfFour", "TopBitOnly", "AllOnes", "TopWordOnly" })
        {
            // 41 and 640: on the mega-kernel, 32(n + floor(n / 2)) bits lie just below 31 * 2^k, so its iterations
            // have no spare one.
            foreach (var n in new[] { 2, 3, 7, 31, 32, 33, 41, 127, 640, 1000, 5000 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // The approximate inverse root x of a normalized operand (bit length 32n or 32n - 1) is never above
    // floor(2^B / sqrt(a)) and at most ApproximateInverseSqrtMaxError below it; the oracle is
    // floor(2^B / sqrt(a)) = isqrt(floor(2^(2B) / a)). ApproximateInverseSqrt always uses the mega-kernel.
    // mutant: GpuBigIntDivRem.Sqrt.cs:79 two Newton iterations fewer for the approximate contract (14 inputs of
    //         n >= 31; one fewer survives: the count keeps a spare iteration)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ApproximateInverseSqrtInputs))]
    public void ApproximateInverseSqrt_NormalizedOperand_IsAtMostMaxErrorBelowFloor(string kind, int n)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = BuildApproximateInverseSqrtOperand(kind, n);
        var marginWords = GpuBigIntDivRem.SqrtFloorMarginWords(n - 1);
        using var ctx = new GpuWorkspace((n + marginWords) * 8, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, n);
        using var inverse = new GpuBigUInt(ctx, n + marginWords);

        GpuBigIntDivRem.ApproximateInverseSqrt(operand, inverse, marginWords);

        var b = 32 * (n + marginWords);
        var floor = GpuBigIntSqrtTests.FloorSqrt((BigInteger.One << (2 * b)) / a);
        var below = floor - inverse.ToBigInteger();
        output.WriteLine($"{kind} n={n}: floor - x = {below}");
        Assert.True(
            below >= 0 && below <= GpuBigIntDivRem.ApproximateInverseSqrtMaxError,
            $"{kind} n={n}: floor(2^B / sqrt(a)) - x = {below}");
    }

    // The approximate path skips the final correction: with zero Newton iterations a hand-built guess 5 below the
    // floor comes back unchanged (the exact InverseSqrtMega corrects the same guess to the floor, see
    // GpuBigIntDivRemTests.InverseSqrtMega_HandBuiltGuessWithoutIterations_IsCorrectedToExactFloor). Without this
    // test, running the correction on the approximate path would pass every other test.
    // mutant: KernelAdvancedMathHandler.cs:211 `correctsToFloor: false` -> `true` (the guess is corrected to the floor)
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ApproximateInverseSqrtMega_HandBuiltGuessWithoutIterations_IsLeftUncorrected()
    {
        CudaCapability.EnsureGpuAvailable();
        const int NumWords = 16;
        const int MarginWords = 4;
        const int PrecisionBits = (NumWords + MarginWords) * 32;
        var a = BuildApproximateInverseSqrtOperand("Random", NumWords);
        var guess = GpuBigIntSqrtTests.FloorSqrt((BigInteger.One << (2 * PrecisionBits)) / a) - 5;
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, NumWords);
        using var inverse = GpuBigUInt.FromBigInteger(ctx, guess, NumWords + MarginWords);

        IGpuAdvancedMathProvider handler = new KernelAdvancedMathHandler();
        handler.ApproximateInverseSqrtMega(ctx, operand, inverse, MarginWords, actualBits: NumWords * 32,
            iterations: 0);

        Assert.Equal(guess, inverse.ToBigInteger());
    }

    private static BigInteger BuildApproximateInverseSqrtOperand(string kind, int n)
    {
        var bits = 32 * n;
        var random = new Random(1301 + 41 * n);
        var bytes = new byte[4 * n + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var randomWords = new BigInteger(bytes) | (BigInteger.One << (bits - 1));
        return kind switch
        {
            "Random" => randomWords,
            "PowerOfFour" => BigInteger.One << (bits - 2),
            "TopBitOnly" => BigInteger.One << (bits - 1),
            "AllOnes" => (BigInteger.One << bits) - 1,
            "TopWordOnly" => (randomWords >> (bits - 32)) << (bits - 32),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown inverse square root input."),
        };
    }
}
