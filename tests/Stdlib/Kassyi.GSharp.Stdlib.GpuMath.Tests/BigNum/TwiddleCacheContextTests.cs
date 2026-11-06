using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The twiddle factors of the 2-prime NTT are device memory of the context they were uploaded in: a division in one
/// workspace context never reads the factors of another, including one that has been destroyed.
/// </summary>
/// <remarks>
/// Properties: (1) q * b + r == a and 0 &lt;= r &lt; b for a division in the shared context after one in a disposed
/// isolated workspace; (2) the cache hands out different factors per context and per execution mode and the same factors
/// within one; (3) disposing an isolated workspace drops its factors; (4) a failed upload is not kept.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
public class TwiddleCacheContextTests
{
    // 10^300 has 997 bits; 32 words take the Newton-Raphson reciprocal that reads the 2-prime NTT twiddles
    private const int Words = 32;

    // Any power of two up to the NTT limit; the tests compare which factors the cache hands out, not their values
    private const int TransformLength = 64;

    private static readonly BigInteger s_dividend = BigInteger.Pow(10, 300);
    private static readonly BigInteger s_divisor = BigInteger.Pow(10, 20) + 7;

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void DivRem_InSharedContextAfterIsolatedWorkspace_IsCorrect()
    {
        CudaCapability.EnsureGpuAvailable();
        if (!IsolatedTestProcess.IsChild(nameof(DivRem_InSharedContextAfterIsolatedWorkspace_IsCorrect)))
        {
            // A read of freed device memory makes the context unusable, so the divisions run in a child process
            IsolatedTestProcess.Run(typeof(TwiddleCacheContextTests),
                nameof(DivRem_InSharedContextAfterIsolatedWorkspace_IsCorrect));
            return;
        }

        using (var isolated = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot, useIsolatedContext: true))
        using (isolated.ActivateContext())
        {
            AssertDivRem(isolated);
        }

        using (var shared = new GpuWorkspace(64))
        {
            AssertDivRem(shared);
        }

        using (var second = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot, useIsolatedContext: true))
        using (second.ActivateContext())
        {
            AssertDivRem(second);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GetOrCreateTwiddleFactors_KeepsOneSetPerContextAndExecutionMode()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;

        using var shared = new GpuWorkspace(64);
        var inShared = handler.GetOrCreateTwiddleFactors(shared, TransformLength);
        Assert.Same(inShared, handler.GetOrCreateTwiddleFactors(shared, TransformLength));

        using (var isolated = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot, useIsolatedContext: true))
        {
            using (isolated.ActivateContext())
            {
                var inIsolated = handler.GetOrCreateTwiddleFactors(isolated, TransformLength);
                Assert.NotSame(inShared, inIsolated);
                Assert.NotEqual(inShared.Forward1, inIsolated.Forward1);
                Assert.Same(inIsolated, handler.GetOrCreateTwiddleFactors(isolated, TransformLength));
            }

            Assert.Equal(1, handler.CountTwiddleFactors(isolated.IsolatedContext.Id));
            isolated.Dispose();
            Assert.Equal(0, handler.CountTwiddleFactors(isolated.IsolatedContext.Id));
        }

        // In CPU fallback mode the factors are host memory and must not be handed to a GPU launch, nor the reverse
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            Assert.NotSame(inShared, handler.GetOrCreateTwiddleFactors(shared, TransformLength));
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }

        Assert.Same(inShared, handler.GetOrCreateTwiddleFactors(shared, TransformLength));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void GetOrCreateTwiddleFactors_AfterFailedUpload_TriesAgain()
    {
        CudaCapability.EnsureGpuAvailable();
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
        using var shared = new GpuWorkspace(64);

        // Length 0 fails while computing the root of unity; a kept failure would rethrow the same exception object
        var first = Assert.Throws<DivideByZeroException>(() => handler.GetOrCreateTwiddleFactors(shared, 0));
        var second = Assert.Throws<DivideByZeroException>(() => handler.GetOrCreateTwiddleFactors(shared, 0));
        Assert.NotSame(first, second);
    }

    private static void AssertDivRem(GpuWorkspace workspace)
    {
        using var a = new GpuBigUInt(workspace, ToWords(s_dividend));
        using var b = new GpuBigUInt(workspace, ToWords(s_divisor));
        using var q = new GpuBigUInt(workspace, Words);
        using var r = new GpuBigUInt(workspace, Words);

        GpuBigIntDivRem.DivRem(a, b, q, r);

        var quotient = q.ToBigInteger();
        var remainder = r.ToBigInteger();
        Assert.Equal(s_dividend, quotient * s_divisor + remainder);
        Assert.True(remainder >= 0 && remainder < s_divisor, $"remainder {remainder} is outside [0, {s_divisor})");
        Assert.Equal(BigInteger.DivRem(s_dividend, s_divisor), (quotient, remainder));
    }

    private static uint[] ToWords(BigInteger value)
    {
        var words = new uint[Words];
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        Buffer.BlockCopy(bytes, 0, words, 0, Math.Min(bytes.Length, Words * 4));
        return words;
    }
}
