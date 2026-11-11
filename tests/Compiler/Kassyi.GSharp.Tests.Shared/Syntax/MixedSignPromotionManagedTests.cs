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
    public void Promote_ManagedUIntMixedWithInt_ComputesInInt64() =>
        MixedSignPromotionScenarios.IntPairs(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Promote_ManagedUIntMixedWithShort_ComputesInInt64() =>
        MixedSignPromotionScenarios.ShortPairs(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Promote_ManagedUIntMixedWithSByte_ComputesInInt64() =>
        MixedSignPromotionScenarios.SBytePairs(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Promote_ManagedConstantOperands_FollowsCSharpConversions() =>
        MixedSignPromotionScenarios.ConstantOperands(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Promote_ManagedMixedOperandsAcrossBarriers_ComputesInInt64() =>
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
