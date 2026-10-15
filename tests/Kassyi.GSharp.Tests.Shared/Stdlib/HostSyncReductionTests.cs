using System.Numerics;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// Validates that helper code which uploads small host data no longer drains the workspace stream before returning.
/// </summary>
/// <remarks>
/// A pageable <c>cuMemcpyHtoDAsync_v2</c> returns once the host buffer is staged, so the buffer may be returned to the
/// <see cref="System.Buffers.ArrayPool{T}"/> immediately and no stream synchronization is needed. A
/// <see cref="StreamGate"/> on the workspace's own stream makes any remaining drain visible: the call would only return
/// after the gate opened.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class HostSyncReductionTests
{
    private const int KBits = 512;
    private const int KWords = 16;
    private const int Iterations = 10;

    private static readonly TimeSpan GateHoldTime = TimeSpan.FromMilliseconds(500);

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ReciprocalMega_DoesNotWaitForQueuedWorkOnOwnStream()
    {
        SkipIfNoDevice();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1024);
        var advancedMath = new AotAdvancedMathHandler();
        var divisor = CreateDivisor();

        // Warm-up builds the per-length twiddle cache, whose one-time upload is allowed to synchronize.
        RunReciprocal(workspace, advancedMath, divisor);

        using var target = GpuBigUInt.FromBigInteger(workspace, divisor, KWords);
        using var result = GpuBigUInt.FromBigInteger(workspace, BigInteger.One << KBits, ResultWords);

        using var gate = StreamGate.Enqueue(workspace.Stream, GateHoldTime);
        Reciprocal(workspace, advancedMath, target, result);
        var returnedWhileStreamHeld = !gate.IsReleased;

        // Let the queued kernel run so that the stream-ordered upload is verified through the numeric result.
        gate.Dispose();
        workspace.Provider.SynchronizeStream(workspace.Stream);

        Assert.True(returnedWhileStreamHeld,
            "Uploading the iteration constant must not drain the workspace stream before launching the kernel.");
        AssertReciprocal(divisor, result.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ReciprocalMega_ConstantUploadIsOrderedBeforeKernel_ResultStaysCorrectAcrossRepeats()
    {
        SkipIfNoDevice();

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1024);
        var advancedMath = new AotAdvancedMathHandler();
        var divisor = CreateDivisor();

        // Back-to-back calls reuse pooled host buffers and device temporaries without any host-side drain between them.
        for (var i = 0; i < 8; i++)
        {
            AssertReciprocal(divisor, RunReciprocal(workspace, advancedMath, divisor));
        }
    }

    private static int ResultWords => ((2 * KBits) + 31) / 32;

    private static BigInteger CreateDivisor()
    {
        var bytes = new byte[KBits / 8];
        new Random(42).NextBytes(bytes);
        bytes[^1] |= 0x80;
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static BigInteger RunReciprocal(GpuWorkspace workspace, AotAdvancedMathHandler advancedMath,
        BigInteger divisor)
    {
        using var target = GpuBigUInt.FromBigInteger(workspace, divisor, KWords);
        using var result = GpuBigUInt.FromBigInteger(workspace, BigInteger.One << KBits, ResultWords);
        Reciprocal(workspace, advancedMath, target, result);
        return result.ToBigInteger();
    }

    private static void Reciprocal(GpuWorkspace workspace, AotAdvancedMathHandler advancedMath, GpuBigUInt target,
        GpuBigUInt result) =>
        advancedMath.ReciprocalMega(workspace, target, result, marginWords: 2, numBits: KBits, marginBits: 0,
            iterations: Iterations, targetBits: 2 * KBits);

    private static void AssertReciprocal(BigInteger divisor, BigInteger actual)
    {
        var expected = (BigInteger.One << (2 * KBits)) / divisor;

        // Newton-Raphson may differ from the exact quotient in the least significant bits.
        var difference = BigInteger.Abs(expected - actual);
        Assert.True(difference <= 2, $"Reciprocal difference too large. Expected: {expected}, Actual: {actual}");
    }

    private static void SkipIfNoDevice()
    {
        // The fallback flag is only populated by driver initialization, so it must run before the flag is read.
        GpuKernelLoader.EnsureInitialized();
        Skip.If(GpuKernelLoader.IsCpuFallbackMode, "Requires a CUDA device.");
    }
}
