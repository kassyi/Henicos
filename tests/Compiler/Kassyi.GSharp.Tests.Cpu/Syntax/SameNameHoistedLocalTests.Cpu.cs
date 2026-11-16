using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SameNameHoistedLocalTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SiblingLoopBarrierKernel_ManagedSiblingLoopsAcrossBarrier_KeepsEachLoopLocal() =>
        RunManaged(() => AssertSiblingLoopBarrier(static (first, second) => SameNameHoistedLocalKernels.LaunchSiblingLoopBarrierKernel(first, second, blockDimX: Length)));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PartlyHoistedNameKernel_ManagedPartlyHoistedScopes_KeepsEachBlockLocal() =>
        RunManaged(() => AssertPartlyHoistedName(static (first, second) => SameNameHoistedLocalKernels.LaunchPartlyHoistedNameKernel(first, second, blockDimX: Length)));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void WarpPrefixAndTotalKernel_ManagedWarpPrefixComputation_ComputesPrefixAndTotalPerWarp() =>
        RunManaged(() => AssertWarpPrefixAndTotal(static (first, second) => SameNameHoistedLocalKernels.LaunchWarpPrefixAndTotalKernel(first, second, blockDimX: Length)));

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PlainLoopBesideBarrierLoopKernel_ManagedBesideBarrierLoop_StartsPlainLoopAtItsInitializer() =>
        RunManaged(() => AssertPlainLoopBesideBarrierLoop(static data => SameNameHoistedLocalKernels.LaunchPlainLoopBesideBarrierLoopKernel(data, PlainLoopBound, blockDimX: Length)));

    private static void RunManaged(Action run)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            run();
        }
    }
}
