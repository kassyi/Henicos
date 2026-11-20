using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Floating-point divisions by a constant zero on the native CPU path (Tests.Cpu) and on CUDA (Tests.Cuda).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class ConstZeroDivisorTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runDefault = static launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroConstDivisor_ClassConst_GivesInfinitiesAndNaN() =>
        ConstZeroDivisorScenarios.ClassConst(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroConstDivisor_LocalConst_GivesInfinitiesAndNaN() =>
        ConstZeroDivisorScenarios.LocalConst(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroConstDivisor_NonConstantDividend_GivesInfinities() =>
        ConstZeroDivisorScenarios.NonConstantDividend(s_runDefault);
}
