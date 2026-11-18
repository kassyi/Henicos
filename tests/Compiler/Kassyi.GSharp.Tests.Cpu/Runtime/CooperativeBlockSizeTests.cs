#nullable enable
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Pure unit tests for <see cref="CooperativeBlockSize"/> and <see cref="CooperativeLaunchOccupancy"/> diagnostic
/// formatting without touching the CUDA driver or GPU.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CooperativeBlockSizeTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Choose_FirstCandidateFits_ReturnsLargestCandidate()
    {
        var result = CooperativeBlockSize.Choose(
            CooperativeBlockSize.DefaultCandidates,
            static blockSize => blockSize switch
            {
                1024 => 1,
                768 => 1,
                512 => 2,
                256 => 4,
                _ => 0
            });

        Assert.True(result.IsSupported);
        Assert.Equal(1024, result.BlockSize);
        Assert.Equal(1, result.ActiveBlocksPerSm);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Choose_LargestZeroAndNextFits_ReturnsLargestFittingCandidate()
    {
        var result = CooperativeBlockSize.Choose(
            CooperativeBlockSize.DefaultCandidates,
            static blockSize => blockSize switch
            {
                1024 => 0,
                768 => 1,
                512 => 2,
                256 => 4,
                _ => 0
            });

        Assert.True(result.IsSupported);
        Assert.Equal(768, result.BlockSize);
        Assert.Equal(1, result.ActiveBlocksPerSm);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Choose_NoCandidateFits_ReturnsUnsupportedWithDiagnostics()
    {
        var resources = new CooperativeKernelResources(
            KernelName: "GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel",
            NumRegs: 66,
            StaticSharedMemBytes: 256,
            DynamicSharedMemBytes: 4224,
            DeviceMajor: 8,
            DeviceMinor: 6,
            MaxRegistersPerMultiprocessor: 65536,
            MaxRegistersPerBlock: 65536,
            MaxSharedMemPerMultiprocessor: 102400,
            MaxThreadsPerBlock: 1024);

        var result = CooperativeBlockSize.Choose(
            CooperativeBlockSize.DefaultCandidates,
            static _ => 0,
            in resources);

        Assert.False(result.IsSupported);
        Assert.Equal(0, result.BlockSize);
        Assert.Equal(256, result.MinCandidateBlockSize);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("InverseSqrtNewtonRaphsonMegaKernel", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("66 registers/thread", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("4480 bytes shared memory", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("256 threads", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("65536 registers/SM", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("102400 bytes shared memory/SM", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("sm_86", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Choose_EmptyCandidates_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(static () =>
            CooperativeBlockSize.Choose([], static _ => 1));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Choose_NullCallback_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(static () =>
            CooperativeBlockSize.Choose([1024, 768], null!)); // Intentionally passing null to verify guard
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(-256)]
    public void Choose_NonPositiveCandidate_ThrowsArgumentOutOfRangeException(int invalidCandidate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CooperativeBlockSize.Choose([1024, invalidCandidate], static _ => 1));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CreateUnsupportedException_WithResources_FormatsDiagnosticMessage()
    {
        var resources = new CooperativeKernelResources(
            KernelName: "GpuBigIntDivRemKernels.ReciprocalNewtonRaphsonMegaKernel",
            NumRegs: 72,
            StaticSharedMemBytes: 256,
            DynamicSharedMemBytes: 8448,
            DeviceMajor: 8,
            DeviceMinor: 6,
            MaxRegistersPerMultiprocessor: 65536,
            MaxRegistersPerBlock: 65536,
            MaxSharedMemPerMultiprocessor: 102400,
            MaxThreadsPerBlock: 1024);

        var exception = CooperativeLaunchOccupancy.CreateUnsupportedException(in resources, 256);

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("ReciprocalNewtonRaphsonMegaKernel", exception.Message, StringComparison.Ordinal);
        Assert.Contains("72 registers/thread", exception.Message, StringComparison.Ordinal);
        Assert.Contains("8704 bytes shared memory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("256 threads", exception.Message, StringComparison.Ordinal);
        Assert.Contains("65536 registers/SM", exception.Message, StringComparison.Ordinal);
    }
}
