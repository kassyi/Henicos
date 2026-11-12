#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed C# fallback runs the C# itself, so it negates a uint as a long and converts the branches of a
/// target-typed conditional as well (#671).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class UIntNegationAndConditionalManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UIntNegation_ManagedExecution_IsComputedInLong() =>
        UIntNegationAndConditionalScenarios.Negation(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TargetTypedConditional_ManagedExecution_ConvertsBothBranchesToTargetType() =>
        UIntNegationAndConditionalScenarios.Conditional(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
