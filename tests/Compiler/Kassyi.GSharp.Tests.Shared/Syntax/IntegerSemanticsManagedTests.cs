#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

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
    public void Test_Managed_IntOperators_WrapAndMaskAsInCSharp() =>
        IntegerSemanticsScenarios.IntTriples(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_LongOperators_WrapAndMaskAsInCSharp() =>
        IntegerSemanticsScenarios.LongTriples(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_FloatToIntegerConversions_SaturateAsInDotNet() =>
        IntegerSemanticsScenarios.Conversions(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ThreadBuiltIns_AreSignedInt() =>
        IntegerSemanticsScenarios.SignedBuiltIns(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_LoopCountersNearIntLimits_CountAsInCSharp() =>
        IntegerSemanticsScenarios.LoopCounters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PackedTargetsWithSideEffects_AreWrittenOnce() =>
        IntegerSemanticsScenarios.PackedTargets(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OperatorsAcrossBarriers_KeepCSharpSemantics() =>
        IntegerSemanticsScenarios.AcrossBarrier(s_runManaged);

    private static void RunManaged(Action launch)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
}
#endif
