#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class ContractsAndPtxTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void InlinePtx_FollowedByWriteOnCpuDialect_HaltsBeforeSubsequentStatements()
    {
        uint[] data = [42u];
        var ex = Assert.Throws<NotSupportedException>(
            () => ContractsAndPtxKernels.LaunchInlinePtxFollowedByWriteKernel(data));
        Assert.Contains("Gpu.InlinePtx", ex.Message);
        Assert.Equal(42u, data[0]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void InlinePtx_MultiThreadCpuLaunch_PropagatesSingleNotSupportedException()
    {
        uint[] data = new uint[256];
        var ex = Assert.Throws<NotSupportedException>(
            () => ContractsAndPtxKernels.LaunchInlinePtxKernel(data, gridDimX: 4, blockDimX: 64));
        Assert.Contains("Gpu.InlinePtx", ex.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void InlinePtx_ManagedFallbackLaunch_PropagatesNotSupportedException()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            uint[] data = [0];
            var ex = Assert.Throws<NotSupportedException>(
                () => ContractsAndPtxKernels.LaunchInlinePtxKernel(data));
            Assert.Contains("Gpu.InlinePtx", ex.Message);
        }
    }
}
