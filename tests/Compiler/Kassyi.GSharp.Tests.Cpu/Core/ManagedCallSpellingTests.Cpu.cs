using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class ManagedCallSpellingTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void InThisExtension_ReceiverFieldOfValueAndVariablesManaged_ExecutesSuccessfully()
    {
        var result = new int[4];
        var cells = CreateCells();

        ManagedKernelRun.Run(() => ManagedCallSpellingKernels.LaunchInReceiverKernel(s_values, cells, result,
            blockDimX: 4));

        Assert.Equal(s_inReceiverExpected, result);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void TargetTypedNew_GenericStructWithSharedMemoryManaged_ExecutesSuccessfully()
    {
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedCallSpellingKernels.LaunchTargetTypedSlotKernel(s_values, result,
            blockDimX: 4));

        Assert.Equal(s_targetTypedSlotExpected, result);
    }
}
