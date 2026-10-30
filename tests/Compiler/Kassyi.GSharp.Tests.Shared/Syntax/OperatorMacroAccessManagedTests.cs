#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroAccessKernels"/> on the managed fallback, which calls the private targets
/// through <c>[UnsafeAccessor]</c> stubs (#711). The values are those of <see cref="OperatorMacroAccessTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroAccessManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PrivateTargets_AreCalled()
    {
        var result = new int[3];

        ManagedKernelRun.Run(() => OperatorMacroAccessKernels.LaunchPrivateTargetsKernel([3, 4], result, blockDimX: 1));

        Assert.Equal(OperatorMacroAccessCases.PrivateTargets, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PrivateTargets_FromADeviceFunction_AreCalled()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroAccessKernels.LaunchDeviceFunctionKernel([3, 4], result, blockDimX: 1));

        Assert.Equal(OperatorMacroAccessCases.DeviceFunction, result);
    }
}
#endif
