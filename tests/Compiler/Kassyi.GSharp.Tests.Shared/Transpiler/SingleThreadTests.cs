#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Runs the <c>Gpu.SingleThread</c> kernels of <see cref="SingleThreadKernels"/>. On the CPU dialects each test runs on
/// the managed path and on the default path (the native library when it exists). The body must run exactly once per
/// launch, on the thread whose six indices are all 0, for every block and grid shape (#667, #690).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SingleThreadTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SingleThread_OneDimensionalLaunch_BodyRunsOnceOnFirstThread(bool forceManaged)
    {
        var runs = new int[8];

        Run(forceManaged, () => SingleThreadKernels.LaunchMarkKernel(runs, gridDimX: 2, blockDimX: 4));

        Assert.Equal(OnlyFirst(8), runs);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 4u, 4u, 1u, 1u, 1u, 1u)]
    [InlineData(true, 4u, 4u, 1u, 1u, 1u, 1u)]
    [InlineData(false, 4u, 1u, 2u, 1u, 1u, 1u)]
    [InlineData(true, 4u, 1u, 2u, 1u, 1u, 1u)]
    [InlineData(false, 4u, 1u, 1u, 1u, 2u, 1u)]
    [InlineData(true, 4u, 1u, 1u, 1u, 2u, 1u)]
    [InlineData(false, 4u, 1u, 1u, 1u, 1u, 2u)]
    [InlineData(true, 4u, 1u, 1u, 1u, 1u, 2u)]
    [InlineData(false, 2u, 2u, 2u, 2u, 2u, 2u)]
    [InlineData(true, 2u, 2u, 2u, 2u, 2u, 2u)]
    public void Test_SingleThread_MultiDimensionalLaunch_BodyRunsOnceOnFirstThread(
        bool forceManaged,
        uint blockDimX,
        uint blockDimY,
        uint blockDimZ,
        uint gridDimX,
        uint gridDimY,
        uint gridDimZ)
    {
        var threads = (int)(blockDimX * blockDimY * blockDimZ * gridDimX * gridDimY * gridDimZ);
        var runs = new int[threads];

        Run(forceManaged, () => SingleThreadKernels.LaunchMarkKernel(
            runs,
            gridDimX: gridDimX,
            gridDimY: gridDimY,
            gridDimZ: gridDimZ,
            blockDimX: blockDimX,
            blockDimY: blockDimY,
            blockDimZ: blockDimZ
        ));

        Assert.Equal(OnlyFirst(threads), runs);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SingleThread_AfterBlockBarrier_BodyRunsOnceOnFirstThread(bool forceManaged)
    {
        var runs = new int[16];

        Run(forceManaged, () => SingleThreadKernels.LaunchMarkAfterBarrierKernel(
            runs,
            gridDimX: 2,
            blockDimX: 4,
            blockDimY: 2
        ));

        Assert.Equal(OnlyFirst(16), runs);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SingleThread_AfterEveryThreadWrites_OverwritesFirstElementOnly(bool forceManaged)
    {
        var a = new int[8];

        Run(forceManaged, () => SingleThreadKernels.LaunchOverwriteFirstKernel(a, gridDimX: 2, blockDimX: 4));

        Assert.Equal([100, 1, 1, 1, 1, 1, 1, 1], a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, 4u, 1u, 1u, 2u, 1u, 1u)]
    [InlineData(true, 4u, 1u, 1u, 2u, 1u, 1u)]
    [InlineData(false, 4u, 2u, 1u, 2u, 1u, 1u)]
    [InlineData(true, 4u, 2u, 1u, 2u, 1u, 1u)]
    [InlineData(false, 2u, 2u, 2u, 2u, 2u, 2u)]
    [InlineData(true, 2u, 2u, 2u, 2u, 2u, 2u)]
    public void Test_SingleThread_InDeviceFunctionWithoutBarrier_BodyRunsOnceOnFirstThread(
        bool forceManaged,
        uint blockDimX,
        uint blockDimY,
        uint blockDimZ,
        uint gridDimX,
        uint gridDimY,
        uint gridDimZ)
    {
        var threads = (int)(blockDimX * blockDimY * blockDimZ * gridDimX * gridDimY * gridDimZ);
        var runs = new int[threads];

        // The managed path runs the condition in Kassyi.GSharp.Core here; the launch with every axis 2 needs all six
        // indices in it
        Run(forceManaged, () => SingleThreadKernels.LaunchDeviceFunctionMarkKernel(
            runs,
            gridDimX: gridDimX,
            gridDimY: gridDimY,
            gridDimZ: gridDimZ,
            blockDimX: blockDimX,
            blockDimY: blockDimY,
            blockDimZ: blockDimZ
        ));

        Assert.Equal(OnlyFirst(threads), runs);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 9)]
    [InlineData(true, 9)]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    public void Test_SingleThread_ReturnInBody_LeavesBodyOnly(bool forceManaged, int stop)
    {
        var a = new int[9];

        Run(forceManaged, () => SingleThreadKernels.LaunchReturnInBodyKernel(a, stop, gridDimX: 2, blockDimX: 4));

        Assert.Equal(ExpectedAfterReturn(stop, secondBody: true), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Test_SingleThread_ReturnInBodyAfterBlockBarrier_LeavesBodyOnly(bool forceManaged, int stop)
    {
        var a = new int[9];

        Run(forceManaged, () => SingleThreadKernels.LaunchReturnInBodyAfterBarrierKernel(a, stop, gridDimX: 2, blockDimX: 4));

        Assert.Equal(ExpectedAfterReturn(stop, secondBody: true), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Test_SingleThread_ReturnInDeviceFunctionBody_LeavesBodyOnly(bool forceManaged, int stop)
    {
        var a = new int[9];

        Run(forceManaged, () => SingleThreadKernels.LaunchReturnInDeviceFunctionBodyKernel(a, stop, gridDimX: 2, blockDimX: 4));

        Assert.Equal(ExpectedAfterReturn(stop, secondBody: false), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 3, 6)]
    [InlineData(true, 3, 6)]
    public void Test_SingleThread_ReturnInLocalFunctionOfBody_LeavesLocalFunctionOnly(bool forceManaged, int stop, int first)
    {
        var a = new int[9];

        Run(forceManaged, () => SingleThreadKernels.LaunchReturnInBodyWithLocalFunctionKernel(a, stop, gridDimX: 2, blockDimX: 4));

        Assert.Equal(WithFirst(first), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0, 11)]
    [InlineData(true, 0, 11)]
    [InlineData(false, 1, 10)]
    [InlineData(true, 1, 10)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 2, 1)]
    public void Test_SingleThread_ReturnInNestedBodies_LeavesItsOwnBody(bool forceManaged, int stop, int first)
    {
        var a = new int[9];

        Run(forceManaged, () => SingleThreadKernels.LaunchNestedReturnKernel(a, stop, gridDimX: 2, blockDimX: 4));

        Assert.Equal(WithFirst(first), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_SingleThread_ReturnInBodyInLoopMacros_LeavesBodyOnly(bool forceManaged)
    {
        var a = new int[2];

        Run(forceManaged, () => SingleThreadKernels.LaunchReturnInLoopMacroKernel(a, gridDimX: 1, blockDimX: 1));

        Assert.Equal([2, 3], a);
    }

    // a[0] as given, a[1..8]: every thread adds 2 after the bodies
    private static int[] WithFirst(int first)
    {
        var expected = new int[9];
        expected[0] = first;
        for (var t = 1; t < expected.Length; t++)
        {
            expected[t] = 2;
        }

        return expected;
    }

    // a[0]: the loop of the first body adds 1 per iteration before stop (all 4 when stop is negative or past the loop),
    // the second body adds 10 unless stop is negative; a[1..8]: every thread adds 2 after the bodies
    private static int[] ExpectedAfterReturn(int stop, bool secondBody)
    {
        var expected = new int[9];
        expected[0] = (stop < 0 ? 4 : Math.Min(stop, 4)) + (secondBody && stop >= 0 ? 10 : 0);
        for (var t = 1; t < expected.Length; t++)
        {
            expected[t] = 2;
        }

        return expected;
    }

    // One run, by the thread with linear index 0
    private static int[] OnlyFirst(int threads)
    {
        var expected = new int[threads];
        expected[0] = 1;
        return expected;
    }

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
