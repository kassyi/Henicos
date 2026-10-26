using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>System.Numerics.Vector</c> constructors whose parameters are not the components in order, on the native dialects
/// (#499): each gives the .NET value instead of aggregate initialization.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class VectorConstructorTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = static launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Splat_SetsEveryComponent() => VectorConstructorScenarios.SplatSetsEveryComponent(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Extend_AppendsComponents() => VectorConstructorScenarios.ExtendAppendsComponents(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TargetTypedSplat_SetsEveryComponent() =>
        VectorConstructorScenarios.TargetTypedSplatSetsEveryComponent(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Splat_EvaluatesItsArgumentOnce() =>
        VectorConstructorScenarios.SplatEvaluatesItsArgumentOnce(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PositionalConstructors_KeepTheirMeaning() =>
        VectorConstructorScenarios.PositionalConstructorsKeepTheirMeaning(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TargetTypedUserConstructor_RunsItsBody() =>
        VectorConstructorScenarios.TargetTypedUserConstructorRunsItsBody(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NamedArguments_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedArgumentsReachTheirParameters(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NamedArguments_AreEvaluatedInTheWrittenOrder() =>
        VectorConstructorScenarios.NamedArgumentsAreEvaluatedInTheWrittenOrder(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NamedSpanArguments_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedSpanArgumentsReachTheirParameters(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NamedCallArguments_AreEvaluatedInTheWrittenOrder() =>
        VectorConstructorScenarios.NamedCallArgumentsAreEvaluatedInTheWrittenOrder(s_runNative);
}
