#if GSHARP_CPU
using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The scenarios of <see cref="VectorConstructorTests"/> on the managed fallback, which runs the C# (#499).</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class VectorConstructorManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = ManagedKernelRun.Run;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Splat_ManagedScalarArgument_SetsEveryComponent() =>
        VectorConstructorScenarios.SplatSetsEveryComponent(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Extend_ManagedSmallerVector_AppendsComponents() =>
        VectorConstructorScenarios.ExtendAppendsComponents(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TargetTypedSplat_ManagedImplicitTarget_SetsEveryComponent() =>
        VectorConstructorScenarios.TargetTypedSplatSetsEveryComponent(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Splat_ManagedSideEffectArgument_EvaluatesItsArgumentOnce() =>
        VectorConstructorScenarios.SplatEvaluatesItsArgumentOnce(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PositionalConstructors_ManagedComponentArguments_KeepTheirMeaning() =>
        VectorConstructorScenarios.PositionalConstructorsKeepTheirMeaning(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TargetTypedUserConstructor_ManagedStructInstantiation_RunsItsBody() =>
        VectorConstructorScenarios.TargetTypedUserConstructorRunsItsBody(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NamedArguments_ManagedReorderedParameters_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedArgumentsReachTheirParameters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NamedArguments_ManagedSideEffects_AreEvaluatedInWrittenOrder() =>
        VectorConstructorScenarios.NamedArgumentsAreEvaluatedInTheWrittenOrder(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NamedSpanArguments_ManagedSpanParameters_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedSpanArgumentsReachTheirParameters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NamedCallArguments_ManagedSideEffects_AreEvaluatedInWrittenOrder() =>
        VectorConstructorScenarios.NamedCallArgumentsAreEvaluatedInTheWrittenOrder(s_runManaged);
}
#endif
