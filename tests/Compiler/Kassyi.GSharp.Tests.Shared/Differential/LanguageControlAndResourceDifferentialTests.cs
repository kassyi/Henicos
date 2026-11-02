#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>Gpu.ForUnroll</c>, device iterators (<c>yield return</c> / <c>yield break</c>),
/// local functions, <c>using</c> disposal lifecycle (and S41), <c>MemoryExtensions.Sort</c>,
/// <c>[InlineArray]</c>, <c>fixed</c> buffers, collection expressions, named/optional arguments, and <c>ref</c> returns
/// (Lane D6 Stage 3).
/// </summary>
public class LanguageControlAndResourceDifferentialTests
{
    private const int Rows = 128;

    private static readonly DifferentialCase s_forUnrollAndIterators = new(
        $"{nameof(LanguageControlAndResourceDifferentialKernels)}.{nameof(LanguageControlAndResourceDifferentialKernels.ForUnrollAndDeviceIteratorsKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageControlAndResourceDifferentialKernels.ForUnrollAndIteratorsColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageControlAndResourceDifferentialKernels.LaunchForUnrollAndDeviceIteratorsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageControlAndResourceDifferentialKernels.ForUnrollAndDeviceIteratorsKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_localFunctionsAndUsing = new(
        $"{nameof(LanguageControlAndResourceDifferentialKernels)}.{nameof(LanguageControlAndResourceDifferentialKernels.LocalFunctionsAndUsingLifecycleKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageControlAndResourceDifferentialKernels.LocalFunctionsAndUsingColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageControlAndResourceDifferentialKernels.LaunchLocalFunctionsAndUsingLifecycleKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageControlAndResourceDifferentialKernels.LocalFunctionsAndUsingLifecycleKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_spanSortAndModernSyntax = new(
        $"{nameof(LanguageControlAndResourceDifferentialKernels)}.{nameof(LanguageControlAndResourceDifferentialKernels.SpanSortAndModernSyntaxKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, ys) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .In("ys", ys)
                .Out<int>("r", rows, LanguageControlAndResourceDifferentialKernels.SpanSortAndModernSyntaxColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => LanguageControlAndResourceDifferentialKernels.LaunchSpanSortAndModernSyntaxKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => LanguageControlAndResourceDifferentialKernels.SpanSortAndModernSyntaxKernel(
            b.Get<int>("xs"),
            b.Get<int>("ys"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ForUnrollAndDeviceIterators_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_forUnrollAndIterators);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalFunctionsAndUsingLifecycle_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_localFunctionsAndUsing);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SpanSortAndModernSyntax_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanSortAndModernSyntax);
}
