using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The C++ that the transpiler emits computes uint mixed with int, short or sbyte in long, as C# does (#671), on the
/// GPU and the native CPU build.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class MixedSignPromotionTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UIntMixedWithInt_IsComputedInLong() =>
        MixedSignPromotionScenarios.IntPairs(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UIntMixedWithShort_IsComputedInLong() =>
        MixedSignPromotionScenarios.ShortPairs(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UIntMixedWithSByte_IsComputedInLong() =>
        MixedSignPromotionScenarios.SBytePairs(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConstantOperands_FollowTheCSharpConversions() =>
        MixedSignPromotionScenarios.ConstantOperands(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_MixedOperandsAcrossBarriers_AreComputedInLong() =>
        MixedSignPromotionScenarios.AcrossBarrier(s_runNative);
}
