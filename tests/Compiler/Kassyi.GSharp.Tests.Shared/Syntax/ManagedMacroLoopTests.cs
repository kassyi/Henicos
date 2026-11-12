using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime;
#endif

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>Gpu.ForUnroll</c> and <c>Gpu.GridStrideLoop</c> without a barrier give the same result in every dialect, managed
/// execution included (#489).
/// </summary>
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class ManagedMacroLoopTests : TranspilerTestBase
{
    private const uint BlockSize = 4;
    private const int Count = 10;
    private const uint GridBlocks = 2;
    private const int GridCount = 20;

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_SumKernel_SumsTheIterations() => AssertForUnrollSum();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_ExpressionBodyKernel_SumsTheIterations() => AssertForUnrollExpressionBody();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_RefContextKernel_AccumulatesIntoContext() => AssertForUnrollRefContext();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void GridStride_RefContextPerThread_KeepsContextPerThread() => AssertGridStrideRefContext();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_NestedLoopVariables_UsesEachLoopVariable() => AssertNestedForUnroll();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_AroundGridStrideKernel_RunsEveryRound() => AssertForUnrollAroundGridStride();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_AroundForUnrollKernel_UnrollsPerIndex() => AssertGridStrideAroundForUnroll();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_SameNameAsLocalKernel_KeepsLocal() => AssertForUnrollSameNameAsLocal();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_CompoundStartKernel_RunsIterationsOfStart() => AssertForUnrollCompoundStart();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void BarrierRefContext_ForUnrollKernel_AccumulatesIntoLiftedContext() =>
        AssertBarrierRefContextForUnroll();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_SharedMemoryKernel_DeclaresSharedMemoryInBody() => AssertForUnrollSharedMemory();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceForUnroll_SumKernel_SumsInDeviceFunction() => AssertDeviceForUnroll();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceForUnroll_ContextKernel_AccumulatesInDeviceFunction() => AssertDeviceForUnrollContext();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceGridStride_FillKernel_FillsInDeviceFunction() => AssertDeviceGridStride();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceGridStride_ContextKernel_KeepsContextInDeviceFunction() =>
        AssertDeviceGridStrideContext();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_SameNameAsLocalKernel_KeepsLocal() => AssertGridStrideSameNameAsLocal();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_ContextSameNameAsLocalKernel_KeepsLocal() =>
        AssertGridStrideContextSameNameAsLocal();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_VisitCountKernel_VisitsEveryElementOnceOverGrid() =>
        AssertVisitsOnce(LaunchGridStrideVisitCount);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void DeviceGridStride_VisitCountKernel_VisitsEveryElementOnceOverGrid() =>
        AssertVisitsOnce(LaunchDeviceGridStrideVisitCount);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_RefContextStepping_StepsByWholeGrid() =>
        AssertRunningSumOverTheGrid(LaunchGridStrideRefContext);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void DeviceGridStride_ContextStepping_StepsByWholeGrid() =>
        AssertRunningSumOverTheGrid(LaunchDeviceGridStrideContext);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_NearIntMaxKernel_RunsNoIteration() => AssertForUnrollNearIntMax();

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_ManagedSumKernel_SumsTheIterations() => RunManaged(AssertForUnrollSum);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_ManagedExpressionBodyKernel_SumsTheIterations() =>
        RunManaged(AssertForUnrollExpressionBody);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_ManagedRefContextKernel_AccumulatesIntoContext() =>
        RunManaged(AssertForUnrollRefContext);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void GridStride_ManagedRefContextPerThread_KeepsContextPerThread() =>
        RunManaged(AssertGridStrideRefContext);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_ManagedNestedLoopVariables_UsesEachLoopVariable() => RunManaged(AssertNestedForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_ManagedAroundGridStrideKernel_RunsEveryRound() =>
        RunManaged(AssertForUnrollAroundGridStride);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_ManagedAroundForUnrollKernel_UnrollsPerIndex() =>
        RunManaged(AssertGridStrideAroundForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_ManagedSameNameAsLocalKernel_KeepsLocal() =>
        RunManaged(AssertForUnrollSameNameAsLocal);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void BarrierForUnroll_ManagedWithInnerForUnroll_ComputesBothLoops() =>
        RunManaged(static () =>
        {
            int[] buffer = new int[BlockSize];
            int[] output = new int[BlockSize];

            // The outer loop holds a barrier and is expanded before lowering;
            // the inner one is rewritten for managed execution
            ForUnrollBarrierKernels.LaunchNestedForUnrollKernel(buffer, output, blockDimX: BlockSize);

            Assert.Equal([48, 50, 52, 46], output);
        });

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_ManagedCompoundStartKernel_RunsIterationsOfStart() =>
        RunManaged(AssertForUnrollCompoundStart);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void BarrierRefContext_ManagedForUnrollKernel_AccumulatesIntoLiftedContext() =>
        RunManaged(AssertBarrierRefContextForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_ManagedSharedMemoryKernel_DeclaresSharedMemoryInBody() =>
        RunManaged(AssertForUnrollSharedMemory);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceForUnroll_ManagedSumKernel_SumsInDeviceFunction() => RunManaged(AssertDeviceForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceForUnroll_ManagedContextKernel_AccumulatesInDeviceFunction() =>
        RunManaged(AssertDeviceForUnrollContext);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceGridStride_ManagedFillKernel_FillsInDeviceFunction() =>
        RunManaged(AssertDeviceGridStride);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceGridStride_ManagedContextKernel_KeepsContextInDeviceFunction() =>
        RunManaged(AssertDeviceGridStrideContext);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_ManagedSameNameAsLocalKernel_KeepsLocal() =>
        RunManaged(AssertGridStrideSameNameAsLocal);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_ManagedContextSameNameAsLocalKernel_KeepsLocal() =>
        RunManaged(AssertGridStrideContextSameNameAsLocal);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_ManagedVisitCountKernel_VisitsEveryElementOnceOverGrid() =>
        RunManaged(static () => AssertVisitsOnce(LaunchGridStrideVisitCount));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void DeviceGridStride_ManagedVisitCountKernel_VisitsEveryElementOnceOverGrid() =>
        RunManaged(static () => AssertVisitsOnce(LaunchDeviceGridStrideVisitCount));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void GridStride_ManagedRefContextStepping_StepsByWholeGrid() =>
        RunManaged(static () => AssertRunningSumOverTheGrid(LaunchGridStrideRefContext));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void DeviceGridStride_ManagedContextStepping_StepsByWholeGrid() =>
        RunManaged(static () => AssertRunningSumOverTheGrid(LaunchDeviceGridStrideContext));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_ManagedNearIntMaxKernel_RunsNoIteration() => RunManaged(AssertForUnrollNearIntMax);

    private static void RunManaged(Action run)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            run();
        }
    }
#endif

    private static void AssertForUnrollSum()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchForUnrollSumKernel(output, blockDimX: BlockSize);

        // 0 + 1 + 2 + 3
        Assert.Equal([6, 6, 6, 6], output);
    }

    private static void AssertForUnrollExpressionBody()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchForUnrollExpressionBodyKernel(output, blockDimX: BlockSize);

        // tid + 2 + 3 + 4
        Assert.Equal([9, 10, 11, 12], output);
    }

    private static void AssertForUnrollRefContext()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchForUnrollRefContextKernel(output, blockDimX: BlockSize);

        // tid + 1 + 2 + 3
        Assert.Equal([6, 7, 8, 9], output);
    }

    private static void AssertGridStrideRefContext()
    {
        int[] output = new int[Count];

        ManagedMacroLoopKernels.LaunchGridStrideRefContextKernel(output, Count, blockDimX: BlockSize);

        // Every thread adds up the indices it visits: thread 0 visits 0, 4, 8; thread 1 visits 1, 5, 9; ...
        Assert.Equal([0, 1, 2, 3, 4, 6, 8, 10, 12, 15], output);
    }

    private static void AssertNestedForUnroll()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchNestedForUnrollKernel(output, blockDimX: BlockSize);

        // outer in 0..2, inner in 0..1: 2 * 10 * (0 + 1 + 2) + 3 * (0 + 1)
        Assert.Equal([63, 63, 63, 63], output);
    }

    private static void AssertForUnrollAroundGridStride()
    {
        int[] output = new int[Count];

        ManagedMacroLoopKernels.LaunchForUnrollAroundGridStrideKernel(output, Count, blockDimX: BlockSize);

        // Rounds add 1, 2 and 3 to every element
        Assert.Equal([6, 6, 6, 6, 6, 6, 6, 6, 6, 6], output);
    }

    private static void AssertGridStrideAroundForUnroll()
    {
        int[] output = new int[Count];

        ManagedMacroLoopKernels.LaunchGridStrideAroundForUnrollKernel(output, Count, blockDimX: BlockSize);

        // i * (0 + 1 + 2)
        Assert.Equal([0, 3, 6, 9, 12, 15, 18, 21, 24, 27], output);
    }

    private static void AssertForUnrollSameNameAsLocal()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchForUnrollSameNameAsLocalKernel(output, blockDimX: BlockSize);

        // The loop variable is not the local i: 0 + 1 + 2 + tid
        Assert.Equal([3, 4, 5, 6], output);
    }

    private static void AssertForUnrollCompoundStart()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchForUnrollCompoundStartKernel(output, blockDimX: BlockSize);

        // Start 1 << 1 = 2, three iterations: 2 + 3 + 4
        Assert.Equal([9, 9, 9, 9], output);
    }

    private static void AssertBarrierRefContextForUnroll()
    {
        int[] buffer = new int[BlockSize];
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchBarrierRefContextForUnrollKernel(buffer, output, blockDimX: BlockSize);

        // tid + (1 + 2 + 3) + 3 * ((tid + 1) % 4)
        Assert.Equal([9, 13, 17, 9], output);
    }

    private static void AssertForUnrollSharedMemory()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchForUnrollSharedMemoryKernel(output, blockDimX: BlockSize);

        // (tid + 0) + (tid + 1)
        Assert.Equal([1, 3, 5, 7], output);
    }

    private static void AssertDeviceForUnroll()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchDeviceForUnrollKernel(output, blockDimX: BlockSize);

        // tid + 0 + 1 + 2 + 3
        Assert.Equal([6, 7, 8, 9], output);
    }

    private static void AssertDeviceForUnrollContext()
    {
        int[] output = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchDeviceForUnrollContextKernel(output, blockDimX: BlockSize);

        // tid * 10 + 1 + 2 + 3
        Assert.Equal([6, 16, 26, 36], output);
    }

    private static void AssertDeviceGridStride()
    {
        int[] output = new int[Count];

        ManagedMacroLoopKernels.LaunchDeviceGridStrideKernel(output, Count, blockDimX: BlockSize);

        Assert.Equal([100, 101, 102, 103, 104, 105, 106, 107, 108, 109], output);
    }

    private static void AssertDeviceGridStrideContext()
    {
        int[] output = new int[Count];

        ManagedMacroLoopKernels.LaunchDeviceGridStrideContextKernel(output, Count, blockDimX: BlockSize);

        // Every thread adds up the indices it visits, as in AssertGridStrideRefContext
        Assert.Equal([0, 1, 2, 3, 4, 6, 8, 10, 12, 15], output);
    }

    private static void LaunchGridStrideVisitCount(int[] data, int count) =>
        ManagedMacroLoopKernels.LaunchGridStrideVisitCountKernel(
            data, count, gridDimX: GridBlocks, blockDimX: BlockSize);

    private static void LaunchDeviceGridStrideVisitCount(int[] data, int count) =>
        ManagedMacroLoopKernels.LaunchDeviceGridStrideVisitCountKernel(
            data, count, gridDimX: GridBlocks, blockDimX: BlockSize);

    private static void LaunchGridStrideRefContext(int[] output, int count) =>
        ManagedMacroLoopKernels.LaunchGridStrideRefContextKernel(
            output, count, gridDimX: GridBlocks, blockDimX: BlockSize);

    private static void LaunchDeviceGridStrideContext(int[] output, int count) =>
        ManagedMacroLoopKernels.LaunchDeviceGridStrideContextKernel(
            output, count, gridDimX: GridBlocks, blockDimX: BlockSize);

    private static void AssertGridStrideSameNameAsLocal()
    {
        int[] data = new int[Count];
        int[] seen = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchGridStrideSameNameAsLocalKernel(data, seen, blockDimX: BlockSize);

        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 1, 1], data);
        // The local i of each thread is untouched by the loop variable of the same name
        Assert.Equal([100, 101, 102, 103], seen);
    }

    private static void AssertGridStrideContextSameNameAsLocal()
    {
        int[] data = new int[Count];
        int[] seen = new int[BlockSize];

        ManagedMacroLoopKernels.LaunchGridStrideContextSameNameAsLocalKernel(data, seen, blockDimX: BlockSize);

        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 1, 1], data);
        // Threads 0 and 1 visit three elements, threads 2 and 3 two: local i + 1000 * visits
        Assert.Equal([3100, 3101, 2102, 2103], seen);
    }

    private static void AssertVisitsOnce(Action<int[], int> launch)
    {
        int[] data = new int[GridCount];

        launch(data, GridCount);

        // A step that is shorter than the grid visits an element twice
        int[] expected = new int[GridCount];
        Array.Fill(expected, 1);
        Assert.Equal(expected, data);
    }

    private static void AssertRunningSumOverTheGrid(Action<int[], int> launch)
    {
        int[] output = new int[GridCount];

        launch(output, GridCount);

        // Thread g of the 8 threads adds up the indices g, g + 8, g + 16: output[g] = g, output[g + 8] = 2g + 8 and
        // output[g + 16] = 3g + 24 (for g < 4)
        int[] expected = new int[GridCount];
        for (int g = 0; g < 8; g++)
        {
            int total = 0;
            for (int i = g; i < GridCount; i += 8)
            {
                total += i;
                expected[i] = total;
            }
        }

        Assert.Equal(expected, output);
    }

    private static void AssertForUnrollNearIntMax()
    {
        int[] output = [-1, -1, -1, -1];

        ManagedMacroLoopKernels.LaunchForUnrollNearIntMaxKernel(output, blockDimX: BlockSize);

        // 2147483646 + 4 wraps around in an int, so the loop runs no iteration
        Assert.Equal([0, 0, 0, 0], output);
    }
}
