#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed C# fallback runs the C# itself (or the lowered state machine of a kernel that suspends), so it gives the
/// same integer results as the GPU and the native CPU build (#722, #521, #724).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class IntegerSemanticsManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EvaluateManaged_IntOperators_WrapAndMaskAsInCSharp() =>
        IntegerSemanticsScenarios.IntTriples(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EvaluateManaged_LongOperators_WrapAndMaskAsInCSharp() =>
        IntegerSemanticsScenarios.LongTriples(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EvaluateManaged_FloatToIntegerConversions_SaturatesAsInDotNet() =>
        IntegerSemanticsScenarios.Conversions(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EvaluateManaged_ThreadBuiltIns_AreSignedInt() =>
        IntegerSemanticsScenarios.SignedBuiltIns(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EvaluateManaged_LoopCountersNearIntLimits_CountsAsInCSharp() =>
        IntegerSemanticsScenarios.LoopCounters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EvaluateManaged_PackedTargetsWithSideEffects_WritesOnce() =>
        IntegerSemanticsScenarios.PackedTargets(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EvaluateManaged_OperatorsAcrossBarriers_KeepsCSharpSemantics() =>
        IntegerSemanticsScenarios.AcrossBarrier(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
