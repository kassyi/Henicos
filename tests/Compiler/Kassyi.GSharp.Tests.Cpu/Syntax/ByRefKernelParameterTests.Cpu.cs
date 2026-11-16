using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class ByRefKernelParameterTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void OutStructParameter_ManagedBarrierExecution_PreservesValueAcrossResumes()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var o = new int[4];

            ByRefKernelParameterKernels.LaunchOutStructBarrierKernel(out var s, o, blockDimX: 4);

            Assert.Equal(8, s.A);
            Assert.Equal(1.5f, s.B);
            Assert.Equal([8, 9, 10, 11], o);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RefReadOnlyStructParameter_ManagedExecutionWithAndWithoutBarrier_ReadsValueAccurately()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var s = new ByRefParameterStruct { A = 10, B = 2.5f };
            var oNoBarrier = new int[4];
            var oBarrier = new int[4];

            ByRefKernelParameterKernels.LaunchRefReadOnlyStructNoBarrierKernel(s, oNoBarrier, blockDimX: 4);
            ByRefKernelParameterKernels.LaunchRefReadOnlyStructBarrierKernel(s, oBarrier, blockDimX: 4);

            Assert.Equal([12, 13, 14, 15], oNoBarrier);
            Assert.Equal([22, 23, 24, 25], oBarrier);
        }
    }
}
