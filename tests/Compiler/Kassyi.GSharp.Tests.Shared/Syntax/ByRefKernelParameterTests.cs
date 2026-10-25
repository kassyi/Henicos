using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class ByRefKernelParameterTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefStructParameterWithBarrier_RereadsUpdatedValueAcrossBarrier()
    {
        var s = new ByRefParameterStruct { A = 5, B = 1.5f };
        var o = new int[4];

        ByRefKernelParameterKernels.LaunchRefStructBarrierKernel(ref s, o, blockDimX: 4);

        Assert.Equal(15, s.A);
        Assert.Equal(2.0f, s.B);
        Assert.Equal([15, 16, 17, 18], o);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefIntParameterWithBarrier_RereadsUpdatedValueAcrossBarrier()
    {
        int x = 5;
        var o = new int[4];

        ByRefKernelParameterKernels.LaunchRefIntBarrierKernel(ref x, o, blockDimX: 4);

        Assert.Equal(15, x);
        Assert.Equal([15, 16, 17, 18], o);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OutStructParameterWithBarrier_RereadsUpdatedValueAcrossBarrier()
    {
        var o = new int[4];

        ByRefKernelParameterKernels.LaunchOutStructBarrierKernel(out var s, o, blockDimX: 4);

        Assert.Equal(8, s.A);
        Assert.Equal(1.5f, s.B);
        Assert.Equal([8, 9, 10, 11], o);
    }

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OutStructParameterWithBarrier_ManagedExecution_PreservesValueAcrossResumes()
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;

        try
        {
            var o = new int[4];

            ByRefKernelParameterKernels.LaunchOutStructBarrierKernel(out var s, o, blockDimX: 4);

            Assert.Equal(8, s.A);
            Assert.Equal(1.5f, s.B);
            Assert.Equal([8, 9, 10, 11], o);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
#endif

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefReadOnlyStructParameter_ReadsValueWithAndWithoutBarrier()
    {
        var s = new ByRefParameterStruct { A = 10, B = 2.5f };
        var oNoBarrier = new int[4];
        var oBarrier = new int[4];

        ByRefKernelParameterKernels.LaunchRefReadOnlyStructNoBarrierKernel(s, oNoBarrier, blockDimX: 4);
        ByRefKernelParameterKernels.LaunchRefReadOnlyStructBarrierKernel(s, oBarrier, blockDimX: 4);

        Assert.Equal([12, 13, 14, 15], oNoBarrier);
        Assert.Equal([22, 23, 24, 25], oBarrier);
    }

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefReadOnlyStructParameter_ManagedExecution_ReadsValueWithAndWithoutBarrier()
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;

        try
        {
            var s = new ByRefParameterStruct { A = 10, B = 2.5f };
            var oNoBarrier = new int[4];
            var oBarrier = new int[4];

            ByRefKernelParameterKernels.LaunchRefReadOnlyStructNoBarrierKernel(s, oNoBarrier, blockDimX: 4);
            ByRefKernelParameterKernels.LaunchRefReadOnlyStructBarrierKernel(s, oBarrier, blockDimX: 4);

            Assert.Equal([12, 13, 14, 15], oNoBarrier);
            Assert.Equal([22, 23, 24, 25], oBarrier);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
#endif

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InStructAndSpanParametersWithBarrier_PreserveValuesAcrossBarrier()
    {
        var s = new ByRefParameterStruct { A = 10, B = 2.5f };
        var oIn = new int[4];
        ByRefKernelParameterKernels.LaunchInStructBarrierKernel(s, oIn, blockDimX: 4);
        Assert.Equal([22, 23, 24, 25], oIn);

        int[] spanBacking = [1, 2, 3, 4];
        var oSpan = new int[4];
        ByRefKernelParameterKernels.LaunchSpanBarrierKernel(spanBacking.AsSpan(), oSpan, blockDimX: 4);
        Assert.Equal([2, 4, 6, 8], spanBacking);
        Assert.Equal([4, 6, 8, 2], oSpan);

        int[] roSpanBacking = [10, 20, 30, 40];
        var oRoSpan = new int[4];
        ByRefKernelParameterKernels.LaunchReadOnlySpanBarrierKernel(new ReadOnlySpan<int>(roSpanBacking), oRoSpan, blockDimX: 4);
        Assert.Equal([30, 50, 70, 50], oRoSpan);
    }
}
