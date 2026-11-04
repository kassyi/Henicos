#nullable enable
#if GSHARP_CPU
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>Gpu.GridStrideLoop</c> on the managed path (#712): the rewritten kernels and the Core implementation visit each
/// index once for every launch shape and read the count once, and the start and step saturate at 2^31.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class GridStrideLoopManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GridStrideLoopScenarios.LaunchIndices), MemberType = typeof(GridStrideLoopScenarios))]
    public void Test_Managed_EveryIndex_IsVisitedOnce(int launch) =>
        GridStrideLoopScenarios.CountVisits(s_runManaged, GridStrideLoopScenarios.Get(launch), withContext: false);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GridStrideLoopScenarios.LaunchIndices), MemberType = typeof(GridStrideLoopScenarios))]
    public void Test_Managed_EveryIndex_IsVisitedOnce_WithContext(int launch) =>
        GridStrideLoopScenarios.CountVisits(s_runManaged, GridStrideLoopScenarios.Get(launch), withContext: true);

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Managed_Count_IsReadOnce() => GridStrideLoopScenarios.CountReadOnce(s_runManaged);

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Managed_CountCall_RunsOncePerThread() => GridStrideLoopScenarios.CountCallOnce(s_runManaged);

    // The start and the step of the helpers that every path calls, for launches whose thread count reaches 2^31: no
    // thread can visit an index from there (count <= int.MaxValue), so both stop at 2^31, where index + step cannot
    // wrap around in 32 bits
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // grid (x, y, z), block (x, y, z), block index (x, y, z), thread index (x, y, z), expected start, expected step
    [InlineData(1, 1, 1, 32, 2, 1, 0, 0, 0, 5, 1, 0, 37u, 64u)]
    [InlineData(3, 2, 2, 4, 3, 2, 2, 1, 1, 3, 2, 1, 24u * 11 + 23, 24u * 12)]
    [InlineData(0x7FFFFFFF, 1, 1, 1024, 1, 1, 0, 0, 0, 0, 0, 0, 0u, 0x80000000u)]
    [InlineData(0x200000, 1, 1, 1024, 1, 1, 0x1FFFFF, 0, 0, 1023, 0, 0, 0x7FFFFFFFu, 0x80000000u)]
    [InlineData(0x400000, 1, 1, 1024, 1, 1, 0x200000, 0, 0, 0, 0, 0, 0x80000000u, 0x80000000u)]
    [InlineData(0x7FFFFFFF, 65535, 65535, 1024, 1, 1, 0x7FFFFFFE, 65534, 65534, 1023, 0, 0, 0x80000000u, 0x80000000u)]
    [InlineData(0x10000, 0x8000, 1, 1, 1, 1, 0, 0x7FFF, 0, 0, 0, 0, 0x7FFF0000u, 0x80000000u)]
    public void Test_StartAndStep_SaturateAt2Pow31(
        int gridX, int gridY, int gridZ,
        int blockX, int blockY, int blockZ,
        int blockIdxX, int blockIdxY, int blockIdxZ,
        int threadIdxX, int threadIdxY, int threadIdxZ,
        uint expectedStart, uint expectedStep)
    {
        uint start = 0;
        uint step = 0;
        RunOnContext(() =>
        {
            ManagedGpuContext.GridDimX = gridX;
            ManagedGpuContext.GridDimY = gridY;
            ManagedGpuContext.GridDimZ = gridZ;
            ManagedGpuContext.BlockDimX = blockX;
            ManagedGpuContext.BlockDimY = blockY;
            ManagedGpuContext.BlockDimZ = blockZ;
            ManagedGpuContext.BlockIdxX = blockIdxX;
            ManagedGpuContext.BlockIdxY = blockIdxY;
            ManagedGpuContext.BlockIdxZ = blockIdxZ;
            ManagedGpuContext.ThreadIdxX = threadIdxX;
            ManagedGpuContext.ThreadIdxY = threadIdxY;
            ManagedGpuContext.ThreadIdxZ = threadIdxZ;
            start = ManagedGpuContext.GridStrideStart();
            step = ManagedGpuContext.GridStrideStep();
        });

        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedStep, step);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(5, 5u)]
    [InlineData(0, 0u)]
    [InlineData(-1, 0u)]
    [InlineData(int.MinValue, 0u)]
    [InlineData(int.MaxValue, 0x7FFFFFFFu)]
    public void Test_Count_NegativeRunsNoIteration(int count, uint expected) =>
        Assert.Equal(expected, ManagedGpuContext.GridStrideCount(count));

    private static void RunManaged(Action launch)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }

    // The context fields are thread-static; a fresh thread leaves the test thread's state alone
    private static void RunOnContext(Action action)
    {
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("The managed context check failed", failure);
        }
    }
}
#endif
