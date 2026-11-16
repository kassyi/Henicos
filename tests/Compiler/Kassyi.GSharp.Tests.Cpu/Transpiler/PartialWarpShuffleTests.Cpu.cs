#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public partial class PartialWarpShuffleTests
{
    private const string KernelFile = "PartialWarpShuffleKernels.cu.cs(";

    // How to avoid the read, at the end of the message: a mask that leaves the lane out does not help
    // (PR #751 review). The whole message must arrive, also through the native error slot
    private const string MissingLaneHint =
        "(CUDA leaves the value undefined. Keep the source lane within the block by limiting delta, "
        + "srcLane or laneMask, or launch a multiple of 32 threads; the mask does not limit which lane is read)";

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ShuffleDown_LaneBeyondPartialWarp_ThrowsWithPlace(bool forceManaged)
    {
        var result = new int[9];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownKernel(result, 16u, blockDimX: 9)));

        Assert.Contains("Warp.ShuffleDownSync at " + KernelFile, exception.Message);
        Assert.Contains("reads lane 16 of warp 0, which does not exist in a block of 9 threads", exception.Message);
        Assert.EndsWith(MissingLaneHint, exception.Message);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ShuffleXor_OneLanePastPartialWarp_ThrowsWithPlace(bool forceManaged)
    {
        var result = new int[9];

        // Lane 0 reads lane 8, which exists; lane 1 reads lane 9, which does not
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleXorKernel(result, 8, blockDimX: 9)));

        Assert.Contains("Warp.ShuffleXorSync at " + KernelFile, exception.Message);
        Assert.Contains("reads lane 9 of warp 0, which does not exist in a block of 9 threads", exception.Message);
        Assert.EndsWith(MissingLaneHint, exception.Message);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void ShuffleWidth_NotAPowerOfTwoUpTo32_ThrowsWithPlace(bool forceManaged, int width)
    {
        var result = new int[32];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(
                forceManaged,
                () => PartialWarpShuffleKernels.LaunchShuffleDownWidthKernel(result, width, blockDimX: 32)
            ));

        Assert.Contains("Warp.ShuffleDownSync at " + KernelFile, exception.Message);
        Assert.Contains($"has width {width}; the width must be 1, 2, 4, 8, 16 or 32", exception.Message);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchAny_CallerOutsideMask_ThrowsWithPlace(bool forceManaged)
    {
        var result = new uint[32];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(
                forceManaged,
                () => PartialWarpShuffleKernels.LaunchMatchAnyCallerOutsideMaskKernel(
                    result,
                    0x00000001u,
                    blockDimX: 32
                )
            ));

        Assert.Contains("Gpu.MatchAnySync at " + KernelFile, exception.Message);
        Assert.Contains("without caller lane", exception.Message);
        Assert.Contains("(CUDA leaves the result undefined)", exception.Message);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchAll_CallerOutsideMask_ThrowsWithPlace(bool forceManaged)
    {
        var result = new uint[32];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(
                forceManaged,
                () => PartialWarpShuffleKernels.LaunchMatchAllCallerOutsideMaskKernel(
                    result,
                    0x00000001u,
                    blockDimX: 32
                )
            ));

        Assert.Contains("Gpu.MatchAllSync at " + KernelFile, exception.Message);
        Assert.Contains("without caller lane", exception.Message);
        Assert.Contains("(CUDA leaves the result undefined)", exception.Message);
    }
}
