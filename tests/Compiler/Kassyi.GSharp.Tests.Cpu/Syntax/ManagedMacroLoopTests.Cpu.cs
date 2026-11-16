using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class ManagedMacroLoopTests
{
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
}
