using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Checks the decisions of the driver initialization: the state the driver calls lead to, and where work runs for each
/// state with and without <see cref="GpuKernelLoader.ForceCpuFallback"/>.
/// </summary>
public class CudaDriverStatusTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Classify_InitFailed_FallsBackToTheCpu()
    {
        var classification = CudaDriverStatus.Classify(CUresult.CudaErrorNoDevice, CUresult.CudaErrorNoDevice, 0);

        Assert.Equal(CudaDriverState.CpuFallback, classification.State);
        Assert.Equal($"cuInit failed: {CUresult.CudaErrorNoDevice}", classification.Reason);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Classify_VersionQueryFailed_FallsBackToTheCpu()
    {
        var classification = CudaDriverStatus.Classify(CUresult.CudaSuccess, CUresult.CudaErrorNotInitialized, 13000);

        Assert.Equal(CudaDriverState.CpuFallback, classification.State);
        Assert.Equal($"cuDriverGetVersion failed: {CUresult.CudaErrorNotInitialized}", classification.Reason);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(12000)]
    [InlineData(12080)]
    [InlineData(12090)]
    public void Classify_DriverOlderThanCuda13_IsUnsupportedWithTheMessage(int driverVersion)
    {
        var classification = CudaDriverStatus.Classify(CUresult.CudaSuccess, CUresult.CudaSuccess, driverVersion);

        Assert.Equal(CudaDriverState.Unsupported, classification.State);
        Assert.Equal(CudaDriverVersion.DescribeUnsupported(driverVersion), classification.Reason);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(13000)]
    [InlineData(13040)]
    public void Classify_Cuda13OrNewerDriver_IsUsable(int driverVersion)
    {
        var classification = CudaDriverStatus.Classify(CUresult.CudaSuccess, CUresult.CudaSuccess, driverVersion);

        Assert.Equal(CudaDriverState.Usable, classification.State);
        Assert.Null(classification.Reason);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData((int)CudaDriverState.Usable)]
    [InlineData((int)CudaDriverState.CpuFallback)]
    [InlineData((int)CudaDriverState.Unsupported)]
    public void DecideGpuUse_ForcedCpuFallback_RunsOnTheCpuWithoutTouchingTheDriver(int state)
    {
        var probed = false;

        var use = CudaDriverStatus.DecideGpuUse(true, () =>
        {
            probed = true;
            return (CudaDriverState)state;
        });

        Assert.Equal(GpuUse.Cpu, use);
        Assert.False(probed);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData((int)CudaDriverState.Usable, (int)GpuUse.Gpu)]
    [InlineData((int)CudaDriverState.CpuFallback, (int)GpuUse.Cpu)]
    [InlineData((int)CudaDriverState.Unsupported, (int)GpuUse.Unsupported)]
    public void DecideGpuUse_NotForced_FollowsTheDriverState(int state, int expected)
    {
        var use = CudaDriverStatus.DecideGpuUse(false, () => (CudaDriverState)state);

        Assert.Equal((GpuUse)expected, use);
    }
}
