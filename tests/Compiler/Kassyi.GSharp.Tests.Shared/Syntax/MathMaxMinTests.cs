using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Floating-point <c>Max</c> / <c>Min</c> / <c>Clamp</c> give the .NET values for NaN and signed zeros on the native
/// dialects (#726): the CUDA build and the native CPU build.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class MathMaxMinTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void MaxMin_SinglePrecision_MatchesDotNet() => MathMaxMinScenarios.SingleMaxMin(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void MaxMin_DoublePrecision_MatchesDotNet() => MathMaxMinScenarios.DoubleMaxMin(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Clamp_SinglePrecision_MatchesDotNet() => MathMaxMinScenarios.SingleClamp(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Clamp_DoublePrecision_MatchesDotNet() => MathMaxMinScenarios.DoubleClamp(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Overload_MixedOperands_PicksCSharpOverload() => MathMaxMinScenarios.MixedOperands(s_runNative);
}
