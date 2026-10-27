#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed C# fallback runs the C# itself, or the lowered state machine of a kernel that suspends, so it computes
/// mixed-sign operators in long as well (#671).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class MixedSignPromotionManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_UIntMixedWithInt_IsComputedInLong() =>
        MixedSignPromotionScenarios.IntPairs(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_UIntMixedWithShort_IsComputedInLong() =>
        MixedSignPromotionScenarios.ShortPairs(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_UIntMixedWithSByte_IsComputedInLong() =>
        MixedSignPromotionScenarios.SBytePairs(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ConstantOperands_FollowTheCSharpConversions() =>
        MixedSignPromotionScenarios.ConstantOperands(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_MixedOperandsAcrossBarriers_AreComputedInLong() =>
        MixedSignPromotionScenarios.AcrossBarrier(s_runManaged);

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
