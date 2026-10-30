using System;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// The grid strategy of <c>AsParallel().Sum()</c> in cooperative kernels (#624 F1). Expected values come from the input
/// the test holds, summed on the host with wrapping addition.
/// </summary>
/// <remarks>
/// Properties: (1) every thread of the grid receives the whole sum; (2) every thread receives the same value; (3) the
/// CPU dialect (native and managed) and CUDA agree.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
public class ParallelAggregateGridTests : TranspilerTestBase
{
    public static TheoryData<uint, uint, uint, int, bool> GridCases()
    {
        var data = new TheoryData<uint, uint, uint, int, bool>();
        foreach (var managed in new[] { false, true })
        {
            // G1 (1-D grids, partial warps) and G2 (a 2 x 2 grid: two blocks with the same blockIdx.x)
            foreach (var (gridX, gridY, block) in new (uint, uint, uint)[] { (4, 1, 64), (3, 1, 33), (1, 1, 1), (2, 2, 32) })
            {
                foreach (var length in new[] { 1, 33, 1000, 4097 })
                {
                    data.Add(gridX, gridY, block, length, managed);
                }
            }
        }

        return data;
    }

    public static TheoryData<bool> Paths() => new() { false, true };

    [SkippableTheory]
    [MemberData(nameof(GridCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GridSum_EveryThreadOfTheGridReceivesTheWholeSum(uint gridX, uint gridY, uint block, int length, bool managed)
    {
        var data = Enumerable.Range(1, length).ToArray();
        var result = new int[gridX * gridY * block];
        Array.Fill(result, -1);

        Run(managed, () => ParallelAggregateGridKernels.LaunchGridSumKernel(data, result, gridDimX: gridX, gridDimY: gridY, blockDimX: block));

        // mutant: Prelude.cu __gsharp_grid_reduce stores the partial at blockIdx.x (the two blocks of each column of the
        // 2 x 2 grid share a slot, G2)
        var expected = WrappingSum(data.Where(static x => x % 3 != 0).Select(static x => x * 2).ToArray());
        for (var i = 0; i < result.Length; i++)
        {
            Assert.True(result[i] == expected, $"thread {i} of {gridX} x {gridY} x {block}, length {length}: {result[i]} != {expected}");
        }
    }

    // G3
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridLongSum_EveryThreadReceivesTheWrappedSum(bool managed)
    {
        long[] data = [long.MaxValue, 5, -3, long.MinValue, 1L << 40, 7, 9, 11];
        var result = new long[4 * 32];

        Run(managed, () => ParallelAggregateGridKernels.LaunchGridLongSumKernel(data, result, gridDimX: 4, blockDimX: 32));

        long expected = 0;
        foreach (var value in data)
        {
            expected = unchecked(expected + value);
        }

        Assert.All(result, value => Assert.Equal(expected, value));
    }

    // G4
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridRepeatedSum_OneScratchServesEveryCall(bool managed)
    {
        var a = Enumerable.Range(1, 300).ToArray();
        var b = Enumerable.Range(-50, 77).ToArray();
        var result = new int[4 * 64];

        Run(managed, () => ParallelAggregateGridKernels.LaunchGridRepeatedSumKernel(a, b, result, gridDimX: 4, blockDimX: 64));

        var expected = 0;
        for (var k = 0; k < 3; k++)
        {
            var shift = k;
            expected = unchecked(expected * 7 + WrappingSum(a));
            expected = unchecked(expected * 7 + WrappingSum(b.Select(x => x + shift).ToArray()));
        }

        Assert.All(result, value => Assert.Equal(expected, value));
    }

    // In a cooperative kernel, an input that differs between blocks keeps the Block strategy
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridSegmentSum_EachBlockReceivesItsSegment(bool managed)
    {
        const int M = 50;
        var data = Enumerable.Range(1, 4 * M - 5).ToArray();
        var result = new int[4 * 64];

        Run(managed, () => ParallelAggregateGridKernels.LaunchGridSegmentSumKernel(data, M, result, gridDimX: 4, blockDimX: 64));

        // mutant: ParallelAggregatePlanner.Plan without the grid-scope check of the inputs (the Grid strategy gives every
        // block the sum of all the segments)
        for (var i = 0; i < result.Length; i++)
        {
            Assert.Equal(WrappingSum(data.Skip(i / 64 * M).Take(M).ToArray()), result[i]);
        }
    }

    // Second review (B1): a lambda that reads BlockIndex gives each block its own sum, so the grid does not share it
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridLambdaBlockIndex_EachBlockReceivesItsOwnSum(bool managed)
    {
        var data = Enumerable.Range(1, 1000).ToArray();
        var result = new int[4 * 64];

        Run(managed, () => ParallelAggregateGridKernels.LaunchGridLambdaBlockIndexKernel(data, result, gridDimX: 4, blockDimX: 64));

        // mutant: ParallelAggregatePlanner.ReadsVariantInput without the check of the lambda body (the grid shares it: 501964)
        for (var i = 0; i < result.Length; i++)
        {
            var block = i / 64;
            Assert.Equal(WrappingSum(data.Select(x => x + block).ToArray()), result[i]);
        }
    }

    // Second review (M2): a three-dimensional grid of three-dimensional blocks (2 x 2 x 2 blocks of 7 x 5 x 3 threads)
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridSum3D_EveryThreadOfTheGridReceivesTheWholeSum(bool managed)
    {
        var data = Enumerable.Range(1, 4099).ToArray();
        var result = new int[8 * 105];
        Array.Fill(result, -1);

        Run(managed, () => ParallelAggregateGridKernels.LaunchGridSumKernel(data, result,
            gridDimX: 2, gridDimY: 2, gridDimZ: 2, blockDimX: 7, blockDimY: 5, blockDimZ: 3));

        // mutant: Prelude.cu __gsharp_grid_block_rank without blockIdx.z (the two layers of blocks share slots)
        var expected = WrappingSum(data.Where(static x => x % 3 != 0).Select(static x => x * 2).ToArray());
        Assert.All(result, value => Assert.Equal(expected, value));
    }

    private void Run(bool managed, Action launch)
    {
        Skip.If(managed && IsCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }

    private static int WrappingSum(int[] values)
    {
        var sum = 0;
        foreach (var value in values)
        {
            sum = unchecked(sum + value);
        }

        return sum;
    }
}
