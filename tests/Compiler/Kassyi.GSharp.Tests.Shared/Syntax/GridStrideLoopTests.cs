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
public class GridStrideLoopTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GridStrideLoopScenarios.LaunchIndices), MemberType = typeof(GridStrideLoopScenarios))]
    public void Test_EveryIndex_IsVisitedOnce(int launch) =>
        GridStrideLoopScenarios.CountVisits(s_runNative, GridStrideLoopScenarios.Get(launch), withContext: false);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(GridStrideLoopScenarios.LaunchIndices), MemberType = typeof(GridStrideLoopScenarios))]
    public void Test_EveryIndex_IsVisitedOnce_WithContext(int launch) =>
        GridStrideLoopScenarios.CountVisits(s_runNative, GridStrideLoopScenarios.Get(launch), withContext: true);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Count_IsReadOnce() => GridStrideLoopScenarios.CountReadOnce(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CountCall_RunsOncePerThread() => GridStrideLoopScenarios.CountCallOnce(s_runNative);

#if !GSHARP_CPU
    // 2^31 and 2^32 threads: too many for the CPU paths, which run every thread
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1u << 21, 5000)]
    [InlineData(1u << 22, 5000)]
    public void Test_HugeLaunch_VisitsEveryIndexOnce(uint gridX, int count) =>
        GridStrideLoopScenarios.CountVisitsOnAHugeLaunch(s_runNative, gridX, count);
#endif
}
