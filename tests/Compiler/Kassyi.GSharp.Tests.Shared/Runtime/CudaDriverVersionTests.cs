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
    [Scenario(TestScenario.SadPath)]
    [InlineData(12000, "12.0")]
    [InlineData(12080, "12.8")]
    [InlineData(12090, "12.9")]
    public void ThrowIfUnsupported_DriverOlderThanCuda13_ThrowsNamingBothVersions(int reported, string reportedText)
    {
        var exception = Assert.Throws<NotSupportedException>(() => CudaDriverVersion.ThrowIfUnsupported(reported));

        Assert.Contains($"supports CUDA {reportedText},", exception.Message);
        Assert.Contains("requires a driver for CUDA 13.0 or newer", exception.Message);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(13000)]
    [InlineData(13040)]
    [InlineData(14000)]
    public void ThrowIfUnsupported_Cuda13OrNewerDriver_DoesNotThrow(int reported)
    {
        var exception = Record.Exception(() => CudaDriverVersion.ThrowIfUnsupported(reported));

        Assert.Null(exception);
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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void DriverGetVersion_InstalledDriver_MeetsTheMinimum()
    {
        CudaCapability.EnsureGpuAvailable();

        var result = CudaDriverApi.cuDriverGetVersion(out var version);

        Assert.Equal(CUresult.CudaSuccess, result);
        Assert.True(
            version >= CudaDriverVersion.Minimum,
            $"The driver reports CUDA {CudaDriverVersion.Format(version)}."
        );
    }
}
