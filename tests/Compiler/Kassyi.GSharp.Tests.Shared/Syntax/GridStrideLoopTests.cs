using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>Gpu.GridStrideLoop</c> on the native dialects (#712): the CUDA build and the native CPU build visit each index
/// once for every launch shape and read the count once.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public partial class GridStrideLoopTests : TranspilerTestBase
{
    internal static readonly Action<Action> s_runNative = launch => launch();

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GridStrideLoopScenarios.LaunchIndices), MemberType = typeof(GridStrideLoopScenarios))]
    public void Execute_EveryIndex_VisitsOnce(int launch) =>
        GridStrideLoopScenarios.CountVisits(s_runNative, GridStrideLoopScenarios.Get(launch), withContext: false);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GridStrideLoopScenarios.LaunchIndices), MemberType = typeof(GridStrideLoopScenarios))]
    public void Execute_EveryIndexWithContext_VisitsOnce(int launch) =>
        GridStrideLoopScenarios.CountVisits(s_runNative, GridStrideLoopScenarios.Get(launch), withContext: true);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Execute_CountExpression_IsReadOnce() => GridStrideLoopScenarios.CountReadOnce(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Execute_CountCall_RunsOncePerThread() => GridStrideLoopScenarios.CountCallOnce(s_runNative);
}
