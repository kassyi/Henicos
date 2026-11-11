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
    private const string KernelName = nameof(StackLinqLocalQueryDifferentialKernels.LocalQueryWithLambdaStagesKernel);

    private static readonly DifferentialCase s_localQuery = new(
        $"{nameof(StackLinqLocalQueryDifferentialKernels)}.{KernelName}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows * SliceLen), OutputPolicy.Exact)
            .Out<int>("r", rows, StackLinqLocalQueryDifferentialKernels.LocalQueryColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqLocalQueryDifferentialKernels.LaunchLocalQueryWithLambdaStagesKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) =>
            StackLinqLocalQueryDifferentialKernels.LocalQueryWithLambdaStagesKernel(b.Get<int>("xs"), b.Get<int>("r")),
    };

    [SkippableFact]
    public void StackLinq_LocalQueryWithLambdaStages_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_localQuery);
}
