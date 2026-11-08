namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// The kernels take the correction status as a non-nullable uint[] status, so a launch needs the status words:
// - a workspace that launches kernels (not CPU fallback) always has them, and passes all of them with their length;
// - only a CPU fallback workspace lacks them, and its operations compute on the host without launching a kernel.
// GpuKernelLoader.ForceCpuFallback is AsyncLocal, so each test sets and restores it itself.
[Category(TestCategory.Stdlib)]
public class CorrectionStatusProxyTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuWorkspace_PassesEveryStatusWord()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        var status = workspace.CorrectionStatus;
        Assert.NotNull(status);
        var proxy = workspace.CorrectionStatusProxy;
        Assert.Equal(status.DevicePointer, proxy.Data);
        Assert.Equal(GpuCorrectionStatus.DeviceWords, proxy.Length);
    }

    // mutant: BigIntWideOps.AddSubtract `if (GpuKernelLoader.IsCpuFallbackMode)` -> `if (false)` (the launch gets a
    // null status and throws ArgumentNullException)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void CpuFallbackWorkspace_HasNoStatusAndComputesWithoutALaunch()
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
            Assert.Null(workspace.CorrectionStatus);
            Assert.Equal(default, workspace.CorrectionStatusProxy);

            using var a = new GpuBigUInt(workspace, [0xFFFF_FFFFu, 1u]);
            using var b = new GpuBigUInt(workspace, [1u, 0u]);
            using var sum = new GpuBigUInt(workspace, 2);
            BigIntWideOps.AddSubtract(a, b, sum, subtract: false);

            Assert.Equal(new BigInteger(0x2_0000_0000L), sum.ToBigInteger());
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}
