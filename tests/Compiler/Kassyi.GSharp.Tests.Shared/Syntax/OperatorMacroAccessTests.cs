using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroAccessKernels"/> on the native target (CPU or CUDA), whose values the managed
/// fallback must reproduce (<c>OperatorMacroAccessManagedTests</c>).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class OperatorMacroAccessTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PrivateTargets_AreCalled()
    {
        var result = new int[3];

        OperatorMacroAccessKernels.LaunchPrivateTargetsKernel([3, 4], result, blockDimX: 1);

        Assert.Equal(OperatorMacroAccessCases.PrivateTargets, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PrivateTargets_FromADeviceFunction_AreCalled()
    {
        var result = new int[2];

        OperatorMacroAccessKernels.LaunchDeviceFunctionKernel([3, 4], result, blockDimX: 1);

        Assert.Equal(OperatorMacroAccessCases.DeviceFunction, result);
    }
}

/// <summary>The expected values of <see cref="OperatorMacroAccessKernels"/>.</summary>
internal static class OperatorMacroAccessCases
{
    // 3 + 3, 4 * 4, two calls
    public static readonly int[] PrivateTargets = [6, 16, 2];

    // 3 * 3 + 4 * 4, three calls
    public static readonly int[] DeviceFunction = [25, 3];
}
