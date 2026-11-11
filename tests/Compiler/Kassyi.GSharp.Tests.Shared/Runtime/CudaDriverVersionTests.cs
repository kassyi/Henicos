using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Checks the minimum driver version the runtime requires when it initializes CUDA.
/// </summary>
public class CudaDriverVersionTests
{
    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(12000, false)]
    [InlineData(12090, false)]
    [InlineData(13000, true)]
    [InlineData(13040, true)]
    [InlineData(14000, true)]
    public void IsSupported_ReportedVersion_IsCuda13OrNewer(int reported, bool expected)
    {
        Assert.Equal(expected, CudaDriverVersion.IsSupported(reported));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(12000, "12.0")]
    [InlineData(12080, "12.8")]
    [InlineData(12090, "12.9")]
    public void DescribeUnsupported_DriverOlderThanCuda13_NamesBothVersions(int reported, string reportedText)
    {
        var message = CudaDriverVersion.DescribeUnsupported(reported);

        Assert.Contains($"supports CUDA {reportedText},", message);
        Assert.Contains("requires a driver for CUDA 13.0 or newer", message);
        Assert.Contains($"driver branch {CudaDriverVersion.MinimumDriverBranch} or newer", message);
        Assert.Contains($"{CudaDriverVersion.MinimumLinuxDriver} or newer on Linux", message);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(13000, "13.0")]
    [InlineData(13040, "13.4")]
    [InlineData(12080, "12.8")]
    public void Format_DriverVersion_IsMajorDotMinor(int version, string expected)
    {
        Assert.Equal(expected, CudaDriverVersion.Format(version));
    }

    // Reads the driver directly: GpuKernelLoader.IsDriverAvailable is false on an old driver, so a skip through
    // CudaCapability would hide exactly the machine this test is for
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void DriverGetVersion_InstalledDriver_MeetsTheMinimum()
    {
        CUresult initResult;
        try
        {
            initResult = CudaDriverApi.cuInit(0);
        }
        catch (DllNotFoundException ex)
        {
            throw new SkipException($"No CUDA driver library: {ex.Message}");
        }

        Skip.If(initResult != CUresult.CudaSuccess, $"cuInit failed: {initResult}");

        var result = CudaDriverApi.cuDriverGetVersion(out var version);

        Assert.Equal(CUresult.CudaSuccess, result);
        Assert.True(
            version >= CudaDriverVersion.Minimum,
            $"The driver reports CUDA {CudaDriverVersion.Format(version)}."
        );
        Assert.Null(GpuKernelLoader.DriverUnavailableReason);
    }
}
