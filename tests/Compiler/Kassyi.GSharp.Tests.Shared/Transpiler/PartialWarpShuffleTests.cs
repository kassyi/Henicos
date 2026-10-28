#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

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
public class PartialWarpShuffleTests : TranspilerTestBase
{
    private const string KernelFile = "PartialWarpShuffleKernels.cu.cs(";

    // How to avoid the read, at the end of the message: a mask that leaves the lane out does not help (PR #751 review). The
    // whole message must arrive, also through the native error slot
    private const string MissingLaneHint =
        "(CUDA leaves the value undefined. Keep the source lane within the block by limiting delta, srcLane or laneMask, or launch a multiple of 32 threads; the mask does not limit which lane is read)";

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ShuffleDown_LaneBeyondPartialWarp_ThrowsWithPlace(bool forceManaged)
    {
        SkipUndefinedOnCuda();
        var result = new int[9];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownKernel(result, 16u, blockDimX: 9)));

        Assert.Contains("Warp.ShuffleDownSync at " + KernelFile, exception.Message);
        Assert.Contains("reads lane 16 of warp 0, which does not exist in a block of 9 threads", exception.Message);
        Assert.EndsWith(MissingLaneHint, exception.Message);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ShuffleXor_OneLanePastPartialWarp_ThrowsWithPlace(bool forceManaged)
    {
        SkipUndefinedOnCuda();
        var result = new int[9];

        // Lane 0 reads lane 8, which exists; lane 1 reads lane 9, which does not
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleXorKernel(result, 8, blockDimX: 9)));

        Assert.Contains("Warp.ShuffleXorSync at " + KernelFile, exception.Message);
        Assert.Contains("reads lane 9 of warp 0, which does not exist in a block of 9 threads", exception.Message);
        Assert.EndsWith(MissingLaneHint, exception.Message);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ShuffleDown_FullWarp_ReadsLaneOrKeepsOwnValueOutsideWidth(bool forceManaged)
    {
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownKernel(result, 16u, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(static t => t < 16 ? t + 16 + 1 : t + 1), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ShuffleXor_EveryLaneExists_ReadsPartnerLane(bool forceManaged)
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
    public void Test_ShuffleIdx_SourceLaneOutsideWidth_WrapsInSegment(bool forceManaged, int srcLane)
    {
        const int Width = 8;
        var result = new int[32];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleIdxKernel(result, srcLane, Width, blockDimX: 32));

        Assert.Equal(Enumerable.Range(0, 32).Select(t => (t & ~(Width - 1)) + (srcLane & (Width - 1)) + 1), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void Test_ShuffleWidth_NotAPowerOfTwoUpTo32_ThrowsWithPlace(bool forceManaged, int width)
    {
        SkipUndefinedOnCuda();
        var result = new int[32];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(forceManaged, () => PartialWarpShuffleKernels.LaunchShuffleDownWidthKernel(result, width, blockDimX: 32)));

        Assert.Contains("Warp.ShuffleDownSync at " + KernelFile, exception.Message);
        Assert.Contains($"has width {width}; the width must be 1, 2, 4, 8, 16 or 32", exception.Message);
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
    public void Test_ShuffleWidth_PowerOfTwo_ShiftsInSegment(bool forceManaged, int width)
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
    public void Test_ShuffleXor_PartnerInLaterGroup_KeepsOwnValue(bool forceManaged, int laneMask)
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
    public void Test_ShuffleUp_LanesAtTheStartOfEachGroup_KeepOwnValue(bool forceManaged, uint delta, int width)
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
    public void Test_ShuffleSync_NamedArgumentsInAnyOrder_ReachTheirParameters(bool forceManaged, bool valueByName)
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

    private static IEnumerable<int> Lanes(int first, int count) => Enumerable.Range(first, count);

    // The managed path computes the same lanes as the native prelude (#789)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(9, false)]
    [InlineData(33, false)]
    [InlineData(9, true)]
    [InlineData(33, true)]
    [InlineData(64, true)]
    public void Test_ActiveMask_PartialWarp_HasOnlyLanesThatExist(int threads, bool forceManaged)
    {
        var result = new uint[threads];

        Run(forceManaged, () => PartialWarpShuffleKernels.LaunchActiveMaskKernel(result, blockDimX: (uint)threads));

        Assert.Equal(
            Enumerable.Range(0, threads).Select(t => t < threads / 32 * 32 ? 0xFFFFFFFFu : (1u << (threads % 32)) - 1u),
            result);
    }

    private void SkipUndefinedOnCuda() =>
        Skip.If(IsCudaDialect, "CUDA leaves the result undefined; only the CPU paths check it.");

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
