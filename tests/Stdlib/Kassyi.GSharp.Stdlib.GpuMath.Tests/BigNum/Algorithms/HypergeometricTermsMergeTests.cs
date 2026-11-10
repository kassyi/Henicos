#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

// Properties of HypergeometricTerms.Merge, against a BigInteger oracle computed from the host inputs:
// - P = P_L * P_R, Q = Q_L * Q_R and T = T_L * Q_R + P_L * T_R, for every combination of the signs of P_L, T_L and T_R;
// - T has one word more than the wider of its two products: a sum of two magnitudes needs one more bit (GMP mpz_add
//   reserves the larger size plus one limb), so the carry of T_L * Q_R + P_L * T_R is kept;
// - a T of zero (the two products cancel) is not negative.

/// <summary><see cref="HypergeometricTerms.Merge"/> on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class HypergeometricTermsMergeTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public HypergeometricTermsMergeTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public static TheoryData<int, int, int> Widths() => MergeCases.Widths();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReportedTerms_KeepTheTopBitOfT()
    {
        // left (P = 0xFFFFFFFF, Q = 1, T = 0xFFFFFFFF), right (P = 1, Q = 0xFFFFFFFF, T = 0xFFFFFFFF): T needs 65 bits,
        // which the two-word result lost
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Algorithms/HypergeometricTerms.cs
        // `BigIntWidths.Add(...)` -> `Math.Max(...)` (T as wide as the wider product)
        MergeCases.Check(_workspace,
            new MergeInput(uint.MaxValue, 1, uint.MaxValue, 1, 1, 1),
            new MergeInput(1, uint.MaxValue, uint.MaxValue, 1, 1, 1));
    }

    [Theory]
    [MemberData(nameof(Widths))]
    [Scenario(TestScenario.Boundary)]
    public void Merge_MatchesTheOracle_ForEverySign(int pWords, int qWords, int tWords) =>
        MergeCases.EverySign(_workspace, pWords, qWords, tWords);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CancellingProducts_GiveAPositiveZero() => MergeCases.Cancelling(_workspace);
}

/// <summary><see cref="HypergeometricTerms.Merge"/> on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class HypergeometricTermsMergeCpuFallbackTests
{
    public static TheoryData<int, int, int> Widths() => MergeCases.Widths();

    [Theory]
    [MemberData(nameof(Widths))]
    [Scenario(TestScenario.Boundary)]
    public void Merge_CpuFallbackWithVaryingWords_FollowsTheSameRules(int pWords, int qWords, int tWords)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            MergeCases.EverySign(workspace, pWords, qWords, tWords);
            MergeCases.Cancelling(workspace);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>One side of a merge: values and the word count of each buffer.</summary>
internal readonly record struct MergeInput(
    BigInteger P,
    BigInteger Q,
    BigInteger T,
    int PWords,
    int QWords,
    int TWords
);

/// <summary>Inputs and oracle checks shared by the GPU and CPU fallback tests.</summary>
internal static class MergeCases
{
    public static TheoryData<int, int, int> Widths() => new()
    {
        { 1, 1, 1 },
        { 2, 1, 2 },
        { 1, 2, 33 },
        { 33, 2, 1 },
        { 2, 33, 64 }
    };

    // All-ones magnitudes in every buffer: both products have their top bits set, so their sum carries out of the wider
    // one when the signs agree, and the difference reaches the top word when they do not
    public static void EverySign(GpuWorkspace workspace, int pWords, int qWords, int tWords)
    {
        var p = AllOnes(32 * pWords);
        var q = AllOnes(32 * qWords);
        var t = AllOnes(32 * tWords);

        foreach (var pLeftNegative in new[] { false, true })
        {
            foreach (var tLeftNegative in new[] { false, true })
            {
                foreach (var tRightNegative in new[] { false, true })
                {
                    var left = new MergeInput(pLeftNegative ? -p : p, q, tLeftNegative ? -t : t, pWords, qWords,
                        tWords);

                    // The right side has other widths, so that the two products of T differ in width
                    var right = new MergeInput(AllOnes(32 * tWords), AllOnes(32 * pWords),
                        tRightNegative ? -AllOnes(32 * qWords - 1) : AllOnes(32 * qWords - 1), tWords, pWords, qWords);
                    Check(workspace, left, right);
                }
            }
        }
    }

    // T_L * Q_R = -30 and P_L * T_R = 30: the sum is zero, and zero is not negative although the first product is
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/BigIntWideOps.cs (AddSigned)
    // `!isZero && (...)` -> `(...)` (a cancelled T keeps the sign of the first product)
    public static void Cancelling(GpuWorkspace workspace) =>
        Check(workspace,
            new MergeInput(6, 5, -10, 1, 1, 1),
            new MergeInput(7, 3, 5, 1, 1, 1));

    public static void Check(GpuWorkspace workspace, MergeInput left, MergeInput right)
    {
        using var leftTerms = Create(workspace, left);
        using var rightTerms = Create(workspace, right);
        using var merged = HypergeometricTerms.Merge(workspace, leftTerms, rightTerms);

        var expectedT = left.T * right.Q + left.P * right.T;
        var message = $"left {left}, right {right}";

        // P and Q are the products of the two sides
        Assert.True(left.P * right.P == Read(merged.P), message);
        Assert.True(left.Q * right.Q == merged.Q.ToBigInteger(), message);

        // T is the exact sum, and a zero T has the + sign
        Assert.True(expectedT == Read(merged.T), $"{message}: T {Read(merged.T)}, expected {expectedT}");
        Assert.True(!expectedT.IsZero || !merged.T.IsNegative, message);

        // T has one word more than the wider of T_L * Q_R and P_L * T_R
        var expectedWords = Math.Max(left.TWords + right.QWords, left.PWords + right.TWords) + 1;
        Assert.True(expectedWords == merged.T.Value.NumWords,
            $"{message}: {merged.T.Value.NumWords} words, expected {expectedWords}");
    }

    private static BigInteger AllOnes(int bits) => (BigInteger.One << bits) - 1;

    private static HypergeometricTerms Create(GpuWorkspace workspace, MergeInput input) =>
        new(Signed(workspace, input.P, input.PWords),
            Magnitude(workspace, input.Q, input.QWords),
            Signed(workspace, input.T, input.TWords));

    private static GpuBigInt Signed(GpuWorkspace workspace, BigInteger value, int words) =>
        new(Magnitude(workspace, BigInteger.Abs(value), words), value.Sign < 0);

    private static GpuBigUInt Magnitude(GpuWorkspace workspace, BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return new GpuBigUInt(workspace, limbs);
    }

    private static BigInteger Read(GpuBigInt value)
    {
        var magnitude = value.Value.ToBigInteger();
        return value.IsNegative ? -magnitude : magnitude;
    }
}
