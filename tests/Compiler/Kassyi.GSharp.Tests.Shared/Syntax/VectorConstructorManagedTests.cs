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
    public void Test_Managed_Splat_SetsEveryComponent() =>
        VectorConstructorScenarios.SplatSetsEveryComponent(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Extend_AppendsComponents() =>
        VectorConstructorScenarios.ExtendAppendsComponents(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_TargetTypedSplat_SetsEveryComponent() =>
        VectorConstructorScenarios.TargetTypedSplatSetsEveryComponent(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_Splat_EvaluatesItsArgumentOnce() =>
        VectorConstructorScenarios.SplatEvaluatesItsArgumentOnce(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_PositionalConstructors_KeepTheirMeaning() =>
        VectorConstructorScenarios.PositionalConstructorsKeepTheirMeaning(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_TargetTypedUserConstructor_RunsItsBody() =>
        VectorConstructorScenarios.TargetTypedUserConstructorRunsItsBody(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_NamedArguments_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedArgumentsReachTheirParameters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NamedArguments_AreEvaluatedInTheWrittenOrder() =>
        VectorConstructorScenarios.NamedArgumentsAreEvaluatedInTheWrittenOrder(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NamedSpanArguments_ReachTheirParameters() =>
        VectorConstructorScenarios.NamedSpanArgumentsReachTheirParameters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NamedCallArguments_AreEvaluatedInTheWrittenOrder() =>
        VectorConstructorScenarios.NamedCallArgumentsAreEvaluatedInTheWrittenOrder(s_runManaged);
}
#endif
