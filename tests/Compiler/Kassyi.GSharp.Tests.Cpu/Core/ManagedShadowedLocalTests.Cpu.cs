using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class ManagedShadowedLocalTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void GridStrideLoop_ManagedLambdaLocalShadowsMethodLocal_PreservesOuterAndInnerValues() =>
        RunManaged(ShadowCase.GridStrideLoop);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SingleThread_ManagedLambdaLocalShadowsMethodLocal_PreservesOuterAndInnerValues() =>
        RunManaged(ShadowCase.SingleThread);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ForUnroll_ManagedLambdaLocalShadowsMethodLocal_PreservesOuterAndInnerValues() =>
        RunManaged(ShadowCase.ForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void NestedAndLater_ManagedLambdaLocalsShadowOuterNames_PreservesOuterAndInnerValues() =>
        RunManaged(ShadowCase.NestedAndLater);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void LocalFunction_ManagedShadowsAndIsShadowed_PreservesOuterAndInnerValues() =>
        RunManaged(ShadowCase.LocalFunction);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedVersion_ManagedLambdaLocalShadowsMethodLocal_PreservesOuterAndInnerValues() =>
        RunManaged(ShadowCase.ManagedVersion);

    private static void RunManaged(ShadowCase shadowCase)
    {
        int[] result = [];

        ManagedKernelRun.Run(() => result = shadowCase.Launch());

        Assert.Equal(shadowCase.Expected, result);
    }
}
