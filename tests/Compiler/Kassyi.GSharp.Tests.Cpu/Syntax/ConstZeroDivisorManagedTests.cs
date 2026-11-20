using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Floating-point divisions by a constant zero on the managed CPU path.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class ConstZeroDivisorManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroConstDivisor_ManagedClassConst_GivesInfinitiesAndNaN() =>
        ConstZeroDivisorScenarios.ClassConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroConstDivisor_ManagedLocalConst_GivesInfinitiesAndNaN() =>
        ConstZeroDivisorScenarios.LocalConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroConstDivisor_ManagedNonConstantDividend_GivesInfinities() =>
        ConstZeroDivisorScenarios.NonConstantDividend(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
