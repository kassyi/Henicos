#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The managed C# fallback runs the .NET methods themselves, so it gives the same values as the native dialects (#726).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class MathMaxMinManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SingleMaxMin_MatchesDotNet() => MathMaxMinScenarios.SingleMaxMin(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_DoubleMaxMin_MatchesDotNet() => MathMaxMinScenarios.DoubleMaxMin(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SingleClamp_MatchesDotNet() => MathMaxMinScenarios.SingleClamp(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_DoubleClamp_MatchesDotNet() => MathMaxMinScenarios.DoubleClamp(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_MixedOperands_PickTheCSharpOverload() => MathMaxMinScenarios.MixedOperands(s_runManaged);

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
