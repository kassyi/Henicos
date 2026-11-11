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
    public void Splat_NativeScalarArgument_SetsEveryComponent() =>
        VectorConstructorScenarios.SplatSetsEveryComponent(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Extend_NativeSmallerVector_AppendsComponents() =>
        VectorConstructorScenarios.ExtendAppendsComponents(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TargetTypedSplat_NativeImplicitTarget_SetsEveryComponent() =>
        VectorConstructorScenarios.TargetTypedSplatSetsEveryComponent(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Splat_NativeSideEffectArgument_EvaluatesItsArgumentOnce() =>
        VectorConstructorScenarios.SplatEvaluatesItsArgumentOnce(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PositionalConstructors_NativeComponentArguments_KeepTheirMeaning() =>
        VectorConstructorScenarios.PositionalConstructorsKeepTheirMeaning(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void TargetTypedUserConstructor_NativeStructInstantiation_RunsItsBody() =>
        VectorConstructorScenarios.TargetTypedUserConstructorRunsItsBody(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void NamedArguments_NativeReorderedParameters_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedArgumentsReachTheirParameters(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NamedArguments_NativeSideEffects_AreEvaluatedInWrittenOrder() =>
        VectorConstructorScenarios.NamedArgumentsAreEvaluatedInTheWrittenOrder(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NamedSpanArguments_NativeSpanParameters_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedSpanArgumentsReachTheirParameters(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NamedCallArguments_NativeSideEffects_AreEvaluatedInWrittenOrder() =>
        VectorConstructorScenarios.NamedCallArgumentsAreEvaluatedInTheWrittenOrder(s_runNative);
}
