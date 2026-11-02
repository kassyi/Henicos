#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroRoutingKernels"/> on the managed fallback: every call of a device function
/// that expands an operator macro, directly or through a call, goes to its managed version (#711). The values are those
/// of <see cref="OperatorMacroRoutingTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroRoutingManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_OperatorMacroReachedThroughACall_IsExpanded()
    {
        var result = new int[3];

        ManagedKernelRun.Run(() => OperatorMacroRoutingKernels.LaunchTransitiveKernel([1, 2, 3], result, blockDimX: 1));

        Assert.Equal(OperatorMacroRoutingCases.Transitive, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RecursiveFunction_ExpandsOnEveryLevel()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroRoutingKernels.LaunchRecursionKernel(result, blockDimX: 1));

        Assert.Equal(OperatorMacroRoutingCases.Recursion, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ExtensionFunction_ExpandsInItsReducedCall()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroRoutingKernels.LaunchExtensionKernel([4, 5], result, blockDimX: 1));

        Assert.Equal(OperatorMacroRoutingCases.Extension, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_FunctionCalledFromALambda_Expands()
    {
        var result = new int[1];

        ManagedKernelRun.Run(() => OperatorMacroRoutingKernels.LaunchLambdaKernel([1, 2, 3], result, blockDimX: 1));

        Assert.Equal(OperatorMacroRoutingCases.Lambda, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ExtensionMethodGroup_ExpandsAndCapturesReceiverByValue()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroRoutingKernels.LaunchExtensionMethodGroupKernel([1, 2, 3], [10], [99], result, blockDimX: 1));

        Assert.Equal(OperatorMacroRoutingCases.ExtensionMethodGroup, result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_NestedInPartialOuterClass_Expands()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => RouteOuter.NestedRoutingKernels.LaunchNestedKernel([6, 7], result, blockDimX: 1));

        Assert.Equal(OperatorMacroRoutingCases.Nested, result);
    }
}
#endif
