using System;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <c>AsParallel().Sum()</c> in kernels (#624). Expected values come from the input the test holds, summed on the host with
/// wrapping addition (StackLinq's <c>Sum</c> wraps; System.Linq's is checked).
/// </summary>
/// <remarks>
/// Properties: (1) every calling thread receives the sum of the whole sequence; (2) every thread receives the same value;
/// (3) the CPU dialect (native and managed) and CUDA agree, since each compares with the same host sum.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
public class ParallelAggregateTests : TranspilerTestBase
{
    public static TheoryData<uint, uint, int, bool> EveryThreadCases()
    {
        var data = new TheoryData<uint, uint, int, bool>();
        foreach (var managed in new[] { false, true })
        {
            // Partial warps (1, 31, 33, 48, 100), full warps and blocks, and lengths around a warp and a block
            foreach (var (grid, block) in new (uint, uint)[] { (4, 64), (1, 1), (2, 31), (2, 32), (2, 33), (1, 48), (3, 100), (1, 1024) })
            {
                foreach (var length in new[] { 1, 31, 32, 33, 1024, 1025 })
                {
                    data.Add(grid, block, length, managed);
                }
            }
        }

        return data;
    }

    public static TheoryData<bool> Paths() => new() { false, true };

    [SkippableTheory]
    [MemberData(nameof(EveryThreadCases))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_EveryThreadSum_EveryThreadReceivesTheWholeSum(uint grid, uint block, int length, bool managed)
    {
        var data = Enumerable.Range(1, length).ToArray();
        var result = new int[grid * block];
        Array.Fill(result, -1);

        Run(managed, () => ParallelAggregateKernels.LaunchEveryThreadSumKernel(data, result, gridDimX: grid, blockDimX: block));

        // (1) and (2): every thread holds the whole sum. The CUDA output before #624 left the block partial sums in
        // thread 0 and 0 or partial values elsewhere.
        // mutant: StackLinqFusionRule.TryTranspile chooses the old parallel emission again (block partial sums, thread 0)
        var expected = WrappingSum(data);
        for (var i = 0; i < result.Length; i++)
        {
            Assert.True(result[i] == expected, $"thread {i} of {grid} x {block}, length {length}: {result[i]} != {expected}");
        }
    }

    // The sum wraps as in StackLinq: two int.MaxValue elements give -2
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EveryThreadSum_WrapsOnOverflow(bool managed)
    {
        int[] data = [int.MaxValue, int.MaxValue, 7];
        var result = new int[2 * 64];

        Run(managed, () => ParallelAggregateKernels.LaunchEveryThreadSumKernel(data, result, gridDimX: 2, blockDimX: 64));

        Assert.All(result, value => Assert.Equal(WrappingSum(data), value));
    }

    // A call in a branch that only thread 0 of each block takes: no thread waits for the others
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GuardedSum_EachBlockReceivesTheWholeSum(bool managed)
    {
        var data = Enumerable.Range(1, 1024).ToArray();
        var result = new int[4];

        Run(managed, () => ParallelAggregateKernels.LaunchGuardedSumKernel(data, result, gridDimX: 4, blockDimX: 64));

        Assert.Equal(Enumerable.Repeat(WrappingSum(data), 4), result);
    }

    // Threads that returned before the call do not stop the others
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EarlyExitSum_RemainingThreadsReceiveTheWholeSum(bool managed)
    {
        var data = Enumerable.Range(1, 300).ToArray();
        const int N = 70;
        var result = new int[2 * 64];
        Array.Fill(result, -1);

        Run(managed, () => ParallelAggregateKernels.LaunchEarlyExitSumKernel(data, result, N, gridDimX: 2, blockDimX: 64));

        for (var i = 0; i < result.Length; i++)
        {
            Assert.Equal(i < N ? WrappingSum(data) : -1, result[i]);
        }
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
