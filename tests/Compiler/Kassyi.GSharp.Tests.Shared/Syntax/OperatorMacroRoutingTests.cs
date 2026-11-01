using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroRoutingKernels"/> on the native target (CPU or CUDA), whose values the managed
/// fallback must reproduce (<c>OperatorMacroRoutingManagedTests</c>).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class OperatorMacroRoutingTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OperatorMacroReachedThroughACall_IsExpanded()
    {
        var result = new int[3];

        OperatorMacroRoutingKernels.LaunchTransitiveKernel([1, 2, 3], result, blockDimX: 1);

        Assert.Equal(OperatorMacroRoutingCases.Transitive, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RecursiveFunction_ExpandsOnEveryLevel()
    {
        var result = new int[2];

        OperatorMacroRoutingKernels.LaunchRecursionKernel(result, blockDimX: 1);

        Assert.Equal(OperatorMacroRoutingCases.Recursion, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExtensionFunction_ExpandsInItsReducedCall()
    {
        var result = new int[2];

        OperatorMacroRoutingKernels.LaunchExtensionKernel([4, 5], result, blockDimX: 1);

        Assert.Equal(OperatorMacroRoutingCases.Extension, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_FunctionCalledFromALambda_Expands()
    {
        var result = new int[1];

        OperatorMacroRoutingKernels.LaunchLambdaKernel([1, 2, 3], result, blockDimX: 1);

        Assert.Equal(OperatorMacroRoutingCases.Lambda, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExtensionMethodGroup_ExpandsAndCapturesReceiverByValue()
    {
        var result = new int[2];

        OperatorMacroRoutingKernels.LaunchExtensionMethodGroupKernel([1, 2, 3], [10], [99], result, blockDimX: 1);

        Assert.Equal(OperatorMacroRoutingCases.ExtensionMethodGroup, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NestedInPartialOuterClass_Expands()
    {
        var result = new int[2];

        RouteOuter.NestedRoutingKernels.LaunchNestedKernel([6, 7], result, blockDimX: 1);

        Assert.Equal(OperatorMacroRoutingCases.Nested, result);
    }
}

/// <summary>The expected values of <see cref="OperatorMacroRoutingKernels"/>.</summary>
internal static class OperatorMacroRoutingCases
{
    // (1 + 2) * 10, 3 * 2, one call of the target
    public static readonly int[] Transitive = [30, 6, 1];

    // 4 + 3 + 2 + 1 + 0, one call per level above 0
    public static readonly int[] Recursion = [10, 4];

    // 4 + 5, one call
    public static readonly int[] Extension = [9, 1];

    // (1 + 1) + (2 + 1) + (3 + 1)
    public static readonly int[] Lambda = [9];

    // (10 + 1) + (10 + 2) + (10 + 3) = 36, both directly and after mutating x to 99
    public static readonly int[] ExtensionMethodGroup = [36, 36];

    // 6 + 7, one call
    public static readonly int[] Nested = [13, 1];
}
