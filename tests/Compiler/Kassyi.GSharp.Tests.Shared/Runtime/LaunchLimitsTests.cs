#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <see cref="LaunchLimits"/>: the limits of every GPU (#799), the block count of a launch counted without wrapping
/// (#805), and the thread limit of the managed path's context array (#1443). No launch runs here.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class LaunchLimitsTests
{
    private const string Kernel = "Kernels.TestKernel";

    // The block counts of #805 (2^32 and 2^31 blocks, which wrapped to 0 and to a negative int) and the largest grid of
    // every GPU, far past 2^32
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1u << 20, 1u << 12, 1u, 1L << 32)]
    [InlineData(1u << 20, 2048u, 1u, 1L << 31)]
    [InlineData(1u << 20, 4097u, 1u, (1L << 32) + (1L << 20))]
    [InlineData(int.MaxValue, 65535u, 65535u, int.MaxValue * 65535L * 65535L)]
    [InlineData(int.MaxValue, 1u, 1u, int.MaxValue)]
    [InlineData(1u, 1u, 1u, 1L)]
    public void CheckLaunch_ReturnsTheBlockCountWithoutWrapping(uint gridX, uint gridY, uint gridZ, long expected)
    {
        Assert.Equal(expected, LaunchLimits.CheckLaunch(gridX, gridY, gridZ, 1024, 1, 1, Kernel));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1024u, 1u, 1u)]
    [InlineData(1u, 1024u, 1u)]
    [InlineData(1u, 1u, 64u)]
    [InlineData(16u, 1u, 64u)]
    [InlineData(32u, 32u, 1u)]
    [InlineData(8u, 8u, 16u)]
    public void CheckLaunch_BlockAtTheLimits_Passes(uint blockX, uint blockY, uint blockZ)
    {
        Assert.Equal(65535L * 65535L, LaunchLimits.CheckLaunch(1, 65535, 65535, blockX, blockY, blockZ, Kernel));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, 1u, 1u, 1u, 1u, 1u, "gridDimX")]
    [InlineData(1u, 0u, 1u, 1u, 1u, 1u, "gridDimY")]
    [InlineData(1u, 1u, 0u, 1u, 1u, 1u, "gridDimZ")]
    [InlineData(1u, 1u, 1u, 0u, 1u, 1u, "blockDimX")]
    [InlineData(1u, 1u, 1u, 1u, 0u, 1u, "blockDimY")]
    [InlineData(1u, 1u, 1u, 1u, 1u, 0u, "blockDimZ")]
    [InlineData(0x80000000u, 1u, 1u, 1u, 1u, 1u, "gridDimX")]
    [InlineData(uint.MaxValue, 1u, 1u, 1u, 1u, 1u, "gridDimX")]
    [InlineData(1u, 65536u, 1u, 1u, 1u, 1u, "gridDimY")]
    [InlineData(1u, 1u, 65536u, 1u, 1u, 1u, "gridDimZ")]
    [InlineData(1u, 1u, 1u, 1025u, 1u, 1u, "blockDimX")]
    [InlineData(1u, 1u, 1u, 1u, 1025u, 1u, "blockDimY")]
    [InlineData(1u, 1u, 1u, 1u, 1u, 65u, "blockDimZ")]
    [InlineData(1u, 1u, 1u, 32u, 33u, 1u, "blockDimX")]
    [InlineData(1u, 1u, 1u, 1024u, 1024u, 64u, "blockDimX")]
    [InlineData(1u, 1u, 1u, 1u, 32u, 33u, "blockDimX")]
    public void CheckLaunch_BeyondTheLimits_ThrowsWithTheDimension(uint gridX, uint gridY, uint gridZ, uint blockX,
        uint blockY, uint blockZ, string paramName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchLimits.CheckLaunch(gridX, gridY, gridZ, blockX, blockY, blockZ, Kernel));

        Assert.Equal(paramName, exception.ParamName);
        Assert.StartsWith("G#: ", exception.Message);
        Assert.Contains(Kernel, exception.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchDimension_MaxThreadsPerBlock_IsTheLimitOfEveryGpu()
    {
        Assert.Equal((int)LaunchLimits.MaxThreadsPerBlock, LaunchDimension.MaxThreadsPerBlock);
    }

    // The managed path holds the contexts of the grid in one array of at most Array.MaxLength elements (#1443)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1u, 1u, 1u)]
    [InlineData(1024u, 1u, 1u)]
    [InlineData(32u, 32u, 1u)]
    [InlineData(7u, 3u, 1u)]
    public void GetManagedContextCount_AtTheArrayLimit_ReturnsTheThreadCount(uint blockX, uint blockY, uint blockZ)
    {
        long threadsPerBlock = blockX * blockY * blockZ;
        var blocks = Array.MaxLength / threadsPerBlock;

        Assert.Equal((int)(blocks * threadsPerBlock),
            LaunchLimits.GetManagedContextCount(blocks, blockX, blockY, blockZ, Kernel));
        Assert.Equal(12 * (int)threadsPerBlock,
            LaunchLimits.GetManagedContextCount(12, blockX, blockY, blockZ, Kernel));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1u, 1u, 1u)]
    [InlineData(1024u, 1u, 1u)]
    [InlineData(32u, 32u, 1u)]
    [InlineData(7u, 3u, 1u)]
    public void GetManagedContextCount_BeyondTheArrayLimit_ThrowsNotSupported(uint blockX, uint blockY, uint blockZ)
    {
        long threadsPerBlock = blockX * blockY * blockZ;
        var blocks = Array.MaxLength / threadsPerBlock + 1;

        var exception = Assert.Throws<NotSupportedException>(() =>
            LaunchLimits.GetManagedContextCount(blocks, blockX, blockY, blockZ, Kernel));

        Assert.Contains("managed CPU path", exception.Message);
        Assert.Contains("https://github.com/kassyi/Henicos/issues/1443", exception.Message);
        Assert.Contains(Kernel, exception.Message);
    }

    // The block counts of #805, and the largest grid: every one is past the array, none wraps into it
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1L << 32)]
    [InlineData(1L << 31)]
    [InlineData(int.MaxValue * 65535L * 65535L)]
    public void GetManagedContextCount_GridsOfTwoToThe31BlocksAndMore_ThrowNotSupported(long blocks)
    {
        Assert.Throws<NotSupportedException>(() => LaunchLimits.GetManagedContextCount(blocks, 1, 1, 1, Kernel));
        Assert.Throws<NotSupportedException>(() => LaunchLimits.GetManagedContextCount(blocks, 1024, 1, 1, Kernel));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(101376u, 0, 101376)]
    [InlineData(97280u, 4096, 101376)]
    public void CheckTargetSharedMemory_AtTheLimit_DoesNotThrow(uint sharedMemBytes, int staticBytes, int maxBytes)
    {
        Assert.Null(Record.Exception(() =>
            LaunchLimits.CheckTargetSharedMemory(sharedMemBytes, staticBytes, maxBytes, "sm_86", Kernel)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CheckTargetSharedMemory_OneByteAboveTheLimit_ThrowsNamingTheTarget()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchLimits.CheckTargetSharedMemory(101377u, 0, 101376, "sm_86", Kernel));

        Assert.Equal("sharedMemBytes", exception.ParamName);
        Assert.StartsWith(
            "G#: sharedMemBytes 101377 plus 0 bytes of static shared memory exceeds the 101376 bytes of shared " +
            "memory per block of the target GPU sm_86 (GSharpTargetArchitectures); the GPU rejects the launch with " +
            "CUDA_ERROR_INVALID_VALUE (Kernels.TestKernel).",
            exception.Message);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CheckTargetSharedMemory_StaticPlusDynamicAboveTheLimit_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchLimits.CheckTargetSharedMemory(97281u, 4096, 101376, "sm_86", Kernel));

        Assert.Equal("sharedMemBytes", exception.ParamName);
        Assert.Contains("sharedMemBytes 97281 plus 4096 bytes of static shared memory exceeds the 101376 bytes",
            exception.Message);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(uint.MaxValue, 1)]
    [InlineData(uint.MaxValue, 0)]
    public void CheckTargetSharedMemory_SumBeyond32Bits_ThrowsWithoutWrapping(uint sharedMemBytes, int staticBytes)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchLimits.CheckTargetSharedMemory(sharedMemBytes, staticBytes, 101376, "sm_86", Kernel));

        Assert.Equal("sharedMemBytes", exception.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CheckTargetSharedMemory_NoSharedMemory_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => LaunchLimits.CheckTargetSharedMemory(0u, 0, 101376, "sm_86", Kernel)));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(232448u, 0, 232448, 334848)]
    [InlineData(228352u, 4096, 232448, 334848)]
    public void CheckTargetSharedMemory_WithOversizedHint_AtTheLimit_DoesNotThrow(
        uint sharedMemBytes,
        int staticBytes,
        int maxBytes,
        int oversizedBytes)
    {
        Assert.Null(Record.Exception(() =>
            LaunchLimits.CheckTargetSharedMemory(sharedMemBytes, staticBytes, maxBytes, "sm_107", Kernel,
                oversizedBytes)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CheckTargetSharedMemory_WithOversizedHint_OneByteAboveTheLimit_ThrowsWithHint()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchLimits.CheckTargetSharedMemory(232449u, 0, 232448, "sm_107", Kernel, 334848));

        Assert.Equal("sharedMemBytes", exception.ParamName);
        Assert.Contains(
            "; this GPU holds up to 334848 bytes for a kernel with [GpuKernel(AllowOversizedSharedMemory = true)]",
            exception.Message);
        Assert.Contains("(Kernels.TestKernel)", exception.Message);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CheckTargetSharedMemory_WithoutHint_DoesNotContainHintPhrase()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchLimits.CheckTargetSharedMemory(101377u, 0, 101376, "sm_86", Kernel));

        Assert.Equal("sharedMemBytes", exception.ParamName);
        Assert.DoesNotContain("AllowOversizedSharedMemory", exception.Message);
        Assert.Contains("CUDA_ERROR_INVALID_VALUE (Kernels.TestKernel).", exception.Message);
    }
}
