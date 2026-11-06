#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>Gpu.ForUnroll</c> bodies with a local function or lambda whose parameter is named like the
/// loop or context parameter: on every path the nested parameter keeps the value passed to it (#1266).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class UnrollShadowDifferentialTests
{
    private const int Rows = 128;

    private static readonly DifferentialCase s_unrollShadow = new(
        $"{nameof(UnrollShadowDifferentialKernels)}.{nameof(UnrollShadowDifferentialKernels.UnrollShadowKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("r", rows, UnrollShadowDifferentialKernels.Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => UnrollShadowDifferentialKernels.LaunchUnrollShadowKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => UnrollShadowDifferentialKernels.UnrollShadowKernel(b.Get<int>("xs"), b.Get<int>("r")),
    };

    [SkippableFact]
    public void Test_UnrollShadow_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_unrollShadow);
}
