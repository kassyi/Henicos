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

    // K2: a block of 8 x 4 x 2 threads in a grid of 2 x 2; the shared loop uses the linear rank of all three dimensions
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EveryThreadSum3D_EveryThreadReceivesTheWholeSum(bool managed)
    {
        var data = Enumerable.Range(1, 1000).ToArray();
        var result = new int[4 * 64];
        Array.Fill(result, -1);

        Run(managed, () => ParallelAggregateKernels.LaunchEveryThreadSum3DKernel(data, result,
            gridDimX: 2, gridDimY: 2, blockDimX: 8, blockDimY: 4, blockDimZ: 2));

        // mutant: Prelude.cu __gsharp_block_rank returns threadIdx.x (threads of other y / z take the same elements)
        Assert.All(result, value => Assert.Equal(WrappingSum(data), value));
    }

    // K5: each block sums its own segment; the last segment is short (Take clamps), and m = 0 sums nothing
    [SkippableTheory]
    [InlineData(100, false)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(100, true)]
    [InlineData(1, true)]
    [InlineData(0, true)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SegmentSum_EachBlockReceivesItsSegment(int m, bool managed)
    {
        const int Blocks = 8;
        const int Threads = 64;
        var data = Enumerable.Range(1, Math.Max(1, Blocks * m - 5)).ToArray();
        var result = new int[Blocks * Threads];

        Run(managed, () => ParallelAggregateKernels.LaunchSegmentSumKernel(data, m, result, gridDimX: Blocks, blockDimX: Threads));

        // mutant: the Take clamp (__gsharp_clamp_count) returns the count unclamped (the last block reads past the end)
        for (var i = 0; i < result.Length; i++)
        {
            var block = i / Threads;
            Assert.Equal(WrappingSum(data.Skip(block * m).Take(m).ToArray()), result[i]);
        }
    }

    // K6
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_FilteredProjected_EveryThreadReceivesTheWholeSum(bool managed)
    {
        var data = Enumerable.Range(1, 1000).ToArray();
        var result = new int[3 * 96];

        Run(managed, () => ParallelAggregateKernels.LaunchFilteredProjectedKernel(data, result, gridDimX: 3, blockDimX: 96));

        var expected = WrappingSum(data.Where(static x => x % 3 != 0).Select(static x => x * 2).ToArray());
        Assert.All(result, value => Assert.Equal(expected, value));
    }

    // K7: long, uint and ulong share the elements and wrap as StackLinq does
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_OtherIntegerSums_EveryThreadReceivesTheWrappedSum(bool managed)
    {
        long[] longs = [long.MaxValue, 5, -3, long.MinValue, 1L << 40];
        uint[] uints = [uint.MaxValue, 2, 7, 1u << 31];
        ulong[] ulongs = [ulong.MaxValue, 3, 1UL << 63];
        var longResult = new long[2 * 64];
        var uintResult = new uint[2 * 64];
        var ulongResult = new ulong[2 * 64];

        Run(managed, () =>
        {
            ParallelAggregateKernels.LaunchLongSumKernel(longs, longResult, gridDimX: 2, blockDimX: 64);
            ParallelAggregateKernels.LaunchUIntSumKernel(uints, uintResult, gridDimX: 2, blockDimX: 64);
            ParallelAggregateKernels.LaunchULongSumKernel(ulongs, ulongResult, gridDimX: 2, blockDimX: 64);
        });

        // mutant: the accumulator declared as int (long and ulong sums lose their high bits)
        Assert.All(longResult, value => Assert.Equal(unchecked(long.MaxValue + 5 - 3 + long.MinValue + (1L << 40)), value));
        Assert.All(uintResult, value => Assert.Equal(unchecked(uint.MaxValue + 2u + 7u + (1u << 31)), value));
        Assert.All(ulongResult, value => Assert.Equal(unchecked(ulong.MaxValue + 3UL + (1UL << 63)), value));
    }

    // K7 (float), K8, K10, K11: summed in each thread; the value is still the C# one
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_PerThreadForms_GiveTheCSharpValue(bool managed)
    {
        var data = Enumerable.Range(1, 1000).ToArray();
        var floats = Array.ConvertAll(data, static x => x / 64f);
        var floatResult = new float[2 * 64];
        var orderResult = new int[2 * 64 * 2];
        var b = Enumerable.Range(5000, 37).ToArray();
        var privateResult = new int[2 * 64];
        int[] counter = [0];
        var impureResult = new int[2 * 64 * 2];

        Run(managed, () =>
        {
            ParallelAggregateKernels.LaunchFloatSumKernel(floats, floatResult, gridDimX: 2, blockDimX: 64);
            ParallelAggregateKernels.LaunchOrderDependentSumKernel(data, orderResult, gridDimX: 2, blockDimX: 64);
            ParallelAggregateKernels.LaunchPrivateSourceKernel(data, b, privateResult, gridDimX: 2, blockDimX: 64);
            ParallelAggregateKernels.LaunchImpureLambdaKernel(data, counter, impureResult, gridDimX: 2, blockDimX: 64);
        });

        // The float sum in each thread is the sequential sum, in order (the values k / 64 add without rounding)
        var floatSum = 0f;
        foreach (var value in floats)
        {
            floatSum += value;
        }

        Assert.All(floatResult, value => Assert.Equal(floatSum, value));
        for (var gtid = 0; gtid < 2 * 64; gtid++)
        {
            Assert.Equal(WrappingSum(data.TakeWhile(static x => x < 500).ToArray()), orderResult[gtid * 2]);
            Assert.Equal(WrappingSum(data.Where(static x => x % 2 == 0).Take(5).ToArray()), orderResult[gtid * 2 + 1]);
            Assert.Equal(WrappingSum(gtid % 64 % 2 == 1 ? b : data), privateResult[gtid]);
            Assert.Equal(WrappingSum(data), impureResult[gtid * 2]);
            var tid = gtid % 64;
            Assert.Equal(WrappingSum(data.Where(x => x > tid).ToArray()), impureResult[gtid * 2 + 1]);
        }

        // Every thread called the selector once per element, as in C#
        // mutant: ParallelAggregatePlanner without the side effect check (the selector runs once per element per block)
        Assert.Equal(2 * 64 * data.Length, counter[0]);
    }

    // K9: repeated calls in a loop reuse the shared slots of the reduction
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RepeatedSum_EveryCallGivesTheWholeSum(bool managed)
    {
        var a = Enumerable.Range(1, 300).ToArray();
        var b = Enumerable.Range(-50, 77).ToArray();
        var result = new int[2 * 128];

        Run(managed, () => ParallelAggregateKernels.LaunchRepeatedSumKernel(a, b, result, gridDimX: 2, blockDimX: 128));

        var expected = 0;
        for (var k = 0; k < 3; k++)
        {
            var shift = k;
            expected = unchecked(expected * 7 + WrappingSum(a));
            expected = unchecked(expected * 7 + WrappingSum(b.Select(x => x + shift).ToArray()));
        }

        Assert.All(result, value => Assert.Equal(expected, value));
    }

    // Second review (B1): a lambda that reads ThreadIndex directly, or through a device function, gives each thread its own
    // C# value, so the call runs in each thread (the shared form gave 148270 instead of 5050 for the first one)
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LambdaReadsThreadIndex_EachThreadReceivesItsOwnSum(bool managed)
    {
        var data = Enumerable.Range(1, 100).ToArray();
        var result = new int[2 * 64 * 3];

        Run(managed, () => ParallelAggregateKernels.LaunchLambdaThreadIndexKernel(data, result, gridDimX: 2, blockDimX: 64));

        // mutant: ParallelAggregatePlanner.ReadsVariantInput without the check of the lambda body
        for (var gtid = 0; gtid < 2 * 64; gtid++)
        {
            var tid = gtid % 64;
            Assert.Equal(WrappingSum(data.Select(x => x * (tid + 1)).ToArray()), result[gtid * 3]);
            Assert.Equal(WrappingSum(data.Where(x => x > tid).ToArray()), result[gtid * 3 + 1]);
            Assert.Equal(WrappingSum(data.Select(x => x + tid).ToArray()), result[gtid * 3 + 2]);
        }
    }

    // Re-review (R1): a selector that passes its element to a device function is shared by the block, with the C# value
    [SkippableTheory]
    [MemberData(nameof(Paths))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DeviceFunctionSelector_EveryThreadReceivesTheWholeSum(bool managed)
    {
        var data = Enumerable.Range(1, 300).ToArray();
        var result = new int[2 * 64];

        Run(managed, () => ParallelAggregateKernels.LaunchDeviceFunctionSelectorKernel(data, result, gridDimX: 2, blockDimX: 64));

        Assert.All(result, value => Assert.Equal(WrappingSum(data.Select(static x => x * x).ToArray()), value));
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
