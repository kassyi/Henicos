#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Runs the shuffles of <see cref="PartialWarpShuffleKernels"/>. A shuffle that reads a lane that does not exist in the
/// block, or that has a width other than 1, 2, 4, 8, 16 or 32, is undefined on CUDA; the CPU paths (native and managed)
/// throw <see cref="InvalidOperationException"/> with the place of the shuffle (#666). Those inputs do not run on CUDA.
/// Every defined case runs on CUDA too, against expected values computed here from the CUDA rules (#692).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class PartialWarpShuffleTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ShuffleDown_FullWarp_ReadsLaneOrKeepsOwnValueOutsideWidth(bool forceManaged)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownKernel(result, 16u, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(static t => t < 16 ? t + 16 + 1 : t + 1), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ShuffleXor_EveryLaneExists_ReadsPartnerLane(bool forceManaged)
    {
        var result = new int[16];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleXorKernel(result, 8, blockDimX: 16));

        Assert.Equal(Enumerable.Range(0, 16).Select(static t => (t ^ 8) + 1), result);
    }

    // CUDA reads lane srcLane % width of the caller's segment (#692)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 13)]
    [InlineData(true, 13)]
    [InlineData(false, 31)]
    [InlineData(true, 31)]
    public void ShuffleIdx_SourceLaneOutsideWidth_WrapsInSegment(bool forceManaged, int srcLane)
    {
        const int Width = 8;
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleIdxKernel(result, srcLane, Width, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(t => (t & ~(Width - 1)) + (srcLane & (Width - 1)) + 1), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 32)]
    [InlineData(true, 32)]
    public void ShuffleWidth_PowerOfTwo_ShiftsInSegment(bool forceManaged, int width)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownWidthKernel(result, width, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(t => t % width + 1 < width ? t + 1 + 1 : t + 1), result);
    }

    // CUDA lets a group of width lanes read its own and earlier groups; a later group keeps its own value. The expected
    // values are those of __shfl_xor_sync on the GPU
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 24)]
    [InlineData(true, 24)]
    public void ShuffleXor_PartnerInLaterGroup_KeepsOwnValue(bool forceManaged, int laneMask)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleXorWidthKernel(result, laneMask, 8, blockDimX: 32));

        int[] expected = laneMask == 8
            ? [.. Lanes(1, 8), .. Lanes(1, 8), .. Lanes(17, 8), .. Lanes(17, 8)]
            : [.. Lanes(1, 8), .. Lanes(9, 8), .. Lanes(9, 8), .. Lanes(1, 8)];
        Assert.Equal(expected, result);
    }

    // The values of __shfl_up_sync on the GPU: a lane less than delta into its group keeps its own value
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 3u, 8)]
    [InlineData(true, 3u, 8)]
    [InlineData(false, 16u, 32)]
    [InlineData(true, 16u, 32)]
    public void ShuffleUp_LanesAtStartOfEachGroup_KeepsOwnValue(bool forceManaged, uint delta, int width)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleUpWidthKernel(result, delta, width, blockDimX: 32));

        int[] expected = width == 8
            ? [1, 2, 3, 1, 2, 3, 4, 5, 9, 10, 11, 9, 10, 11, 12, 13, 17, 18, 19, 17, 18, 19, 20, 21, 25, 26, 27, 25, 26, 27, 28, 29]
            : [.. Lanes(1, 16), .. Lanes(1, 16)];
        Assert.Equal(expected, result);
    }

    // __shfl_sync(m, t + 1, 3, 8) on the GPU: 4 x 8, 12 x 8, 20 x 8, 28 x 8
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShuffleSync_NamedArgumentsInAnyOrder_ReachesParameters(bool forceManaged, bool valueByName)
    {
        var result = new int[32];

        Run(forceManaged, () =>
        {
            if (valueByName)
            {
                PartialWarpShuffleKernels.LaunchShuffleNamedValueKernel(result, blockDimX: 32);
            }
            else
            {
                PartialWarpShuffleKernels.LaunchShuffleNamedArgumentsKernel(result, blockDimX: 32);
            }
        });

        Assert.Equal([.. Enumerable.Repeat(4, 8), .. Enumerable.Repeat(12, 8), .. Enumerable.Repeat(20, 8), .. Enumerable.Repeat(28, 8)], result);
    }

    // PTX shfl.sync uses only the low 5 bits of the lane offset and of the lane mask: 33 and 0xFFFFFFE1 shift by 1
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 33u)]
    [InlineData(true, 33u)]
    [InlineData(false, 0xFFFFFFE1u)]
    [InlineData(true, 0xFFFFFFE1u)]
    public void ShuffleDown_DeltaOf32OrMore_UsesLowFiveBits(bool forceManaged, uint delta)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownKernel(result, delta, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(static t => t < 31 ? t + 1 + 1 : t + 1), result);
    }

    // 33 shifts by 1 in a group of 32; 35 shifts by 3 in groups of 8, as delta 3 does
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 33u, 32)]
    [InlineData(true, 33u, 32)]
    [InlineData(false, 35u, 8)]
    [InlineData(true, 35u, 8)]
    public void ShuffleUp_DeltaOf32OrMore_UsesLowFiveBits(bool forceManaged, uint delta, int width)
    {
        var result = new int[32];

        Run(
            forceManaged,
            () => PartialWarpShuffleKernels.LaunchShuffleUpWidthKernel(result, delta, width, blockDimX: 32)
        );

        int shift = (int)(delta & 31u);
        Assert.Equal(Enumerable.Range(0, 32).Select(t => t % width < shift ? t + 1 : t - shift + 1), result);
    }

    // A lane mask of -1 is 31 and one of 40 is 8 on the GPU
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    [InlineData(false, 40)]
    [InlineData(true, 40)]
    public void ShuffleXor_LaneMaskOf32OrMore_UsesLowFiveBits(bool forceManaged, int laneMask)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleXorKernel(result, laneMask, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(t => (t ^ (laneMask & 31)) + 1), result);
    }

    private static IEnumerable<int> Lanes(int first, int count) => Enumerable.Range(first, count);

    // The managed path computes the same lanes as the native prelude (#789)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(9, false)]
    [InlineData(33, false)]
    [InlineData(9, true)]
    [InlineData(33, true)]
    [InlineData(64, true)]
    public void ActiveMask_PartialWarp_HasOnlyLanesThatExist(int threads, bool forceManaged)
    {
        var result = new uint[threads];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchActiveMaskKernel(result, blockDimX: (uint)threads));

        Assert.Equal(
            Enumerable.Range(0, threads).Select(t => t < threads / 32 * 32 ? 0xFFFFFFFFu : (1u << (threads % 32)) - 1u),
            result);
    }

    // The managed path runs a device function that does not suspend as the user's method, so the stub itself computes the
    // mask there (#789)
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(9, false)]
    [InlineData(40, false)]
    [InlineData(9, true)]
    [InlineData(40, true)]
    public void ActiveMask_InDeviceFunction_HasOnlyLanesThatExist(int threads, bool forceManaged)
    {
        var result = new uint[threads];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchActiveMaskViaDeviceKernel(result, blockDimX: (uint)threads));

        Assert.Equal(
            Enumerable.Range(0, threads).Select(t => t < threads / 32 * 32 ? 0xFFFFFFFFu : (1u << (threads % 32)) - 1u),
            result);
    }

    // 5 x 7 = 35 threads: the first warp is full, the second has 3 lanes, whatever the shape of the block
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveMask_TwoDimensionalBlock_FollowsLinearThreadIndex(bool forceManaged)
    {
        var result = new uint[35];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchActiveMask2DKernel(result, blockDimX: 5, blockDimY: 7));

        Assert.Equal([.. Enumerable.Repeat(0xFFFFFFFFu, 32), .. Enumerable.Repeat(7u, 3)], result);
    }

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
