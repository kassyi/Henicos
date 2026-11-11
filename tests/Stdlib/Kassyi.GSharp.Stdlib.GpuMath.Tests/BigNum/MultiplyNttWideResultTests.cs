#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties of GpuBigIntMath.MultiplyNtt into a result buffer wider than the product, pre-filled with 0xFFFFFFFF:
// - the low words hold the exact product (BigInteger oracle from the host inputs);
// - every word above the product is zero, whichever path computes it: the single-block kernel (results of 512 words
//   or fewer), or the NTT launcher with its blocked carry kernel (short transforms), its tiled carry kernel (long
//   transforms) and its squaring path (the same buffer as both operands).

/// <summary><see cref="GpuBigIntMath.MultiplyNtt"/> into a wider, reused result buffer on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class MultiplyNttWideResultTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public MultiplyNttWideResultTests()
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

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/Ntt35ConvolutionLauncher.cs
    // ResolveNtt35Carries without the clear above the product (the launcher cases keep 0xFFFFFFFF there)
    [Theory]
    [MemberData(nameof(WideResultCases.Cases), MemberType = typeof(WideResultCases))]
    [Scenario(TestScenario.Boundary)]
    public void Product_IntoADirtyWiderBuffer_IsExact_AndZeroAbove(int leftWords, int rightWords, int resultWords,
        bool square) =>
        WideResultCases.Check(_workspace, leftWords, rightWords, resultWords, square);
}

/// <summary><see cref="GpuBigIntMath.MultiplyNtt"/> into a wider, reused result buffer on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class MultiplyNttWideResultCpuFallbackTests
{
    [Theory]
    [MemberData(nameof(WideResultCases.Cases), MemberType = typeof(WideResultCases))]
    [Scenario(TestScenario.Boundary)]
    public void MultiplyNtt_CpuFallbackWithVaryingWords_FollowsTheSameRules(
        int leftWords,
        int rightWords,
        int resultWords,
        bool square)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            WideResultCases.Check(workspace, leftWords, rightWords, resultWords, square);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>Inputs and oracle checks shared by the GPU and CPU fallback tests.</summary>
internal static class WideResultCases
{
    public static TheoryData<int, int, int, bool> Cases() => new()
    {
        // Single-block kernel: the control, which cleared the words above already
        { 150, 150, 400, false },
        // NTT launcher, short transform (blocked carry kernel)
        { 40, 40, 600, false },
        // NTT launcher, long transform (tiled carry kernel): the reported case
        { 300, 300, 1400, false },
        // NTT launcher, squaring
        { 300, 300, 1400, true }
    };

    public static void Check(GpuWorkspace workspace, int leftWords, int rightWords, int resultWords, bool square)
    {
        var random = new Random(635 + leftWords * 37 + resultWords);
        var left = RandomWords(random, leftWords);
        var right = square ? left : RandomWords(random, rightWords);

        using var a = new GpuBigUInt(workspace, left);
        using var b = new GpuBigUInt(workspace, right);
        using var result = new GpuBigUInt(workspace, Enumerable.Repeat(uint.MaxValue, resultWords).ToArray());

        GpuBigIntMath.MultiplyNtt(a, square ? a : b, result);

        // The exact product in the low words and zeros above it: the whole buffer equals the product's limbs
        var expected = new uint[resultWords];
        (ToBigInteger(left) * ToBigInteger(right)).WriteLimbs(expected);
        Assert.Equal(expected, result.ToHostArray());
    }

    // The top word is non-zero, so the product reaches the word below the sum of the widths
    private static uint[] RandomWords(Random random, int words)
    {
        var limbs = new uint[words];
        for (var i = 0; i < words; i++)
        {
            limbs[i] = (uint)random.NextInt64(0, 1L << 32);
        }

        limbs[^1] |= 0x80000000u;
        return limbs;
    }

    private static BigInteger ToBigInteger(uint[] limbs)
    {
        var bytes = new byte[limbs.Length * 4 + 1];
        Buffer.BlockCopy(limbs, 0, bytes, 0, limbs.Length * 4);
        return new BigInteger(bytes);
    }
}
