#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Runs the <c>Gpu.SyncGrid(body)</c> kernels of <see cref="SyncGridBodyKernels"/> (#626) with 2 blocks of 32 threads.
/// On the CPU dialects each test runs on the managed path and on the default path (the native library when it exists).
/// The expected values are computed from the inputs, not from the kernel.
/// </summary>
/// <remarks>
/// Both CPU paths run a cooperative kernel in grid phases, so a kernel that reads what another block wrote before a gap
/// runs on the native path as well (#664, found with these tests).
/// </remarks>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SyncGridBodyTests : TranspilerTestBase
{
    private const int Blocks = 2;
    private const int ThreadsPerBlock = 32;
    private const int Threads = Blocks * ThreadsPerBlock;

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_TwoStatements_BodyRuns(bool forceManaged)
    {
        var a = Filled(-1);

        Run(forceManaged, () => SyncGridBodyKernels.LaunchBodyRunsKernel(a, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(1, Threads), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReadAfterGap_SeesOtherBlocksWrites(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchNeighborKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_NestedBlocks_SyncOnlyBetweenTopLevelStatements(bool forceManaged)
    {
        var a = Filled(1);

        Run(forceManaged, () => SyncGridBodyKernels.LaunchNestedKernel(a, Threads, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // The loop makes every element (1 + 1) * 2; thread 0 of block b then adds 1 to elements 2b and 2b + 1
        var expected = Enumerable.Repeat(4, Threads).ToArray();
        for (var block = 0; block < Blocks; block++)
        {
            expected[block * 2] += 1;
            expected[block * 2 + 1] += 1;
        }

        Assert.Equal(expected, a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_EmptiesExplicitSyncAndNestedBodies_RunEveryStatement(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchMixedKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 101), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_LocalAcrossGaps_KeepsItsValue(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchHoistedLocalKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1 + t + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_InsideUniformLoop_SyncsEveryIteration(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchLoopNestedKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // k = 0 and k = 1: the neighbour's value is (t + 1) % n + 1 + k
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 2 * ((t + 1) % Threads + 1) + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_SiblingBodiesWithSameLocalName_KeepTheirOwnValues(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchSiblingLocalsKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 2 * t), a);
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 4 * t), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void Test_SyncGridBody_UniformReturn_SkipsTheRestOfTheBodyOnly(bool forceManaged, int n)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchUniformReturnKernel(a, b, n, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // The statement after the body runs on every thread, whether the body returned or not
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t + 1001), a);
        Assert.Equal(
            Enumerable.Range(0, Threads).Select(t => n == 0 ? 0 : (t + 1) % Threads + 101),
            b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_DivergentReturn_ReturnedThreadsStillSync(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchDivergentReturnKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 2 == 0 ? 0 : (t + 1) % Threads + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInNestedBody_SkipsTheNestedRestOnly(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchNestedReturnKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        var expectedA = Enumerable.Range(0, Threads).Select(static t => t < 32 ? 1 : 2).ToArray();
        Assert.Equal(expectedA, a);
        Assert.Equal(Enumerable.Range(0, Threads).Select(t => expectedA[(t + 1) % Threads]), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInLoopAndLocalFunction_LeaveTheirOwnScopes(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchReturnFromLoopKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // Two iterations add 2, the local function adds 10; b = 7 is skipped, b += 1 runs
        Assert.Equal(Enumerable.Repeat(12, Threads), a);
        Assert.Equal(Enumerable.Repeat(1, Threads), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_DeclarationsAfterReturn_StayVisibleAndSkipInitializers(bool forceManaged)
    {
        const int n = 40;
        var a = Enumerable.Range(0, Threads).Select(static t => t * 3).ToArray();
        var b = new int[Threads];
        var c = new int[Threads];
        var counter = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchDeclarationAfterReturnKernel(a, b, c, counter, n, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // v = a[t], w = 2v, x = 1, y = x + w, k = 5: b = v + y + k = 3v + 6; the returned threads run no initializer
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t < n ? 3 * (t * 3) + 6 : 0), b);
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t < n ? 1 : 0), c);
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t < n ? 1 : 0), counter);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_InDeviceFunctionWithSuspendingCall_SyncsBetweenStatements(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];
        var c = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchDeviceFunctionKernel(a, b, c, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1), b);
        Assert.Equal(
            Enumerable.Range(0, Threads).Select(static t => t - t % ThreadsPerBlock + (t % ThreadsPerBlock + 1) % ThreadsPerBlock),
            c);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_BodyInLoopOfBody_SyncsEveryIteration(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchBodyInLoopOfBodyKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => 2 * ((t + 1) % Threads + 1) + 1 + 1000), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ExpressionBodyIsABody_SyncsBetweenItsStatements(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchExpressionBodyIsBodyKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_OutVariablesAfterReturn_StayVisible(bool forceManaged)
    {
        const int n = 40;
        var a = Enumerable.Range(0, Threads).Select(static t => t * 3).ToArray();
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchOutVariableAfterReturnKernel(a, b, n, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // x = z = 3t + 1, y = 6t, s = z + 1: b = 15t + 4; the threads that returned write nothing
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t < n ? 15 * t + 4 : 0), b);
    }

    // A goto case written for a return inside the user's switch would bind to that switch: to its case 2 here
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInSwitch_LeavesTheBodyNotTheSwitch(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];
        var mode = Enumerable.Range(0, Threads).Select(static t => t % 3).ToArray();

        Run(forceManaged, () => SyncGridBodyKernels.LaunchReturnInSwitchKernel(a, b, mode, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        var expectedA = Enumerable.Range(0, Threads).Select(static t => t % 3 == 2 ? 101 : 1).ToArray();
        Assert.Equal(expectedA, a);
        Assert.Equal(Enumerable.Range(0, Threads).Select(t => t % 3 == 0 ? 0 : expectedA[(t + 1) % Threads]), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInSwitchWithoutMatchingCase_LeavesTheBody(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];
        var mode = Enumerable.Range(0, Threads).Select(static t => t % 3).ToArray();

        Run(forceManaged, () => SyncGridBodyKernels.LaunchReturnInSwitchWithoutCaseKernel(a, b, mode, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 3 == 0 ? 0 : 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInForEach_LeavesTheBody(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];
        int[] values = [5, 10, 70];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchReturnInForEachKernel(a, b, values, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // Thread 5 returns before the first increment, thread 10 after one; the others count the three values
        var expectedA = Enumerable.Range(0, Threads).Select(static t => t == 5 ? 0 : t == 10 ? 1 : 3).ToArray();
        Assert.Equal(expectedA, a);
        Assert.Equal(Enumerable.Range(0, Threads).Select(t => t is 5 or 10 ? 0 : expectedA[(t + 1) % Threads]), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInLabeledStatement_LeavesTheBody(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchReturnInLabeledStatementKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 2 == 0 ? 0 : 1), b);
    }

    // The last statement of the nested body runs only while neither flag is set
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_NestedReturnAfterReturn_GuardsWithBothFlags(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];
        var c = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchNestedReturnAfterReturnKernel(a, b, c, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 2 == 1 && t % 3 != 0 ? 1 : 0), b);
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 2 == 1 ? 5 : 0), c);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_TwoReturnsAndGotoInGuardedStatement_SkipFromEachReturn(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchTwoReturnsKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        // The loop adds 10 in iterations 0 and 2; the goto skips iteration 1
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 2 == 0 ? 0 : 21), a);
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t % 2 == 1 && t % 3 != 0 ? 2 : 0), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGridBody_ReturnInForUnrollBody_EachCopyEndsItsOwnBody(bool forceManaged)
    {
        var a = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchForUnrollReturnKernel(a, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t == 0 ? 1 : 2), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGrid_InDeviceFunction_SeesOtherBlocksWrites(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchDeviceFunctionSyncKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1), b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SyncGrid_TwoDeviceFunctionsDeep_SeesOtherBlocksWrites(bool forceManaged)
    {
        var a = new int[Threads];
        var b = new int[Threads];

        Run(forceManaged, () => SyncGridBodyKernels.LaunchNestedDeviceFunctionSyncKernel(a, b, gridDimX: Blocks, blockDimX: ThreadsPerBlock));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (t + 1) % Threads + 1), b);
    }

    private static int[] Filled(int value) => Enumerable.Repeat(value, Threads).ToArray();

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
