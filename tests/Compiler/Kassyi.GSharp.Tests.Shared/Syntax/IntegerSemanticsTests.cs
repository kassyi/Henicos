using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The C++ that the transpiler emits gives the C# results of signed overflow, shift counts, <c>&gt;&gt;&gt;</c>, the
/// thread built-ins and float-to-integer conversions (#722, #521, #724), on the GPU and the native CPU build.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class IntegerSemanticsTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntOperators_WrapAndMaskAsInCSharp() =>
        IntegerSemanticsScenarios.IntTriples(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LongOperators_WrapAndMaskAsInCSharp() =>
        IntegerSemanticsScenarios.LongTriples(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatToIntegerConversions_SaturateAsInDotNet() =>
        IntegerSemanticsScenarios.Conversions(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ThreadBuiltIns_AreSignedInt() =>
        IntegerSemanticsScenarios.SignedBuiltIns(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LoopCountersNearIntLimits_CountAsInCSharp() =>
        IntegerSemanticsScenarios.LoopCounters(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PackedTargetsWithSideEffects_AreWrittenOnce() =>
        IntegerSemanticsScenarios.PackedTargets(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperatorsAcrossBarriers_KeepCSharpSemantics() =>
        IntegerSemanticsScenarios.AcrossBarrier(s_runNative);
}
