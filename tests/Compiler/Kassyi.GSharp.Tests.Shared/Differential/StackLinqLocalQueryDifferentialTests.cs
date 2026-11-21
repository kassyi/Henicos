#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for unfused StackLinq queries with lambda stages stored in a local (#1086).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class StackLinqLocalQueryDifferentialTests
{
    private const int SliceLen = StackLinqLocalQueryDifferentialKernels.SliceLength;

    internal static readonly DifferentialCase s_localQuery = DiffCases.Create(
        StackLinqLocalQueryDifferentialKernels.LaunchLocalQueryWithLambdaStagesKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows * SliceLen), OutputPolicy.Exact)
            .Out<int>("r", rows, StackLinqLocalQueryDifferentialKernels.LocalQueryColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    [SkippableFact]
    public void StackLinq_LocalQueryWithLambdaStages_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_localQuery);
}
