namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Warning A (GSH0099, #624): an <c>AsParallel()</c> aggregate that runs sequentially in each thread is reported once,
/// at its terminal call, with the reason; an aggregate that the threads of a block can share is not reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class ParallelAggregateAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task PerThreadAggregates_ReportGSH0099WithTheirReason()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/ParallelAggregate/ParallelAggregateWarnings.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // (line, start column, end column) of the terminal call, 0-based, and the reason; SharedKernel (line 12), the
        // cooperative kernel, the block-dependent ones (lines 89 and 117) and DeviceFunctionSelectorKernel (the last one: a
        // selector that passes its element to a device function) have none
        // mutant: ThreadVarianceAnalysis.GetCallVariance without the uniform symbols (the lambda parameter counts as variant)
        // mutant: ParallelAggregatePlanner.Plan without the divergence check (DivergentKernel is not reported)
        (int Line, int Start, int End, ParallelAggregateFallback Reason)[] expected =
        [
            (16, 92, 97, ParallelAggregateFallback.OutsideKernelBody),
            (21, 61, 66, ParallelAggregateFallback.InsideLambda),
            (30, 73, 78, ParallelAggregateFallback.DivergentCall),
            (42, 70, 75, ParallelAggregateFallback.EarlyExit),
            (50, 72, 77, ParallelAggregateFallback.ThreadPrivateSource),
            (56, 92, 97, ParallelAggregateFallback.OrderDependentStage),
            (62, 116, 121, ParallelAggregateFallback.SideEffect),
            (69, 80, 85, ParallelAggregateFallback.ThreadVariantInput),
            (75, 70, 75, ParallelAggregateFallback.UnsupportedElementType),
            // Second review: lambdas that read ThreadIndex or a variant device function directly (B1)
            // mutant: ParallelAggregatePlanner.ReadsVariantInput without the check of the lambda body (shared, wrong values)
            (95, 107, 112, ParallelAggregateFallback.ThreadVariantInput),
            (101, 100, 105, ParallelAggregateFallback.ThreadVariantInput),
            (110, 93, 98, ParallelAggregateFallback.ThreadVariantInput),
            // A switch arm and a for incrementor that only some threads reach (B2)
            // mutant: ParallelAggregatePlanner.IsDivergent with KernelBarrierValidator.GetDivergingCondition only
            (124, 98, 103, ParallelAggregateFallback.DivergentCall),
            (131, 98, 103, ParallelAggregateFallback.DivergentCall),
            // A variant break / continue whose loop holds the call, and a variant return after the call in a loop (M2)
            (145, 55, 60, ParallelAggregateFallback.EarlyExit),
            (166, 55, 60, ParallelAggregateFallback.EarlyExit),
            (177, 75, 80, ParallelAggregateFallback.EarlyExit),
        ];
        var actual = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.ParallelAggregateRunsSequentially.Id)
            .OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            var span = actual[i].Location.GetLineSpan();
            Assert.Equal(GSharpDiagnostics.ParallelAggregateRunsSequentially.Id, actual[i].Id);
            Assert.Equal((expected[i].Line, expected[i].Start), (span.StartLinePosition.Line, span.StartLinePosition.Character));
            Assert.Equal((expected[i].Line, expected[i].End), (span.EndLinePosition.Line, span.EndLinePosition.Character));
            Assert.Contains(ParallelAggregatePlanner.Describe(expected[i].Reason), actual[i].GetMessage());
        }
    }

    // Warning B (GSH0100): an aggregate whose inputs are the same in the whole grid, in a kernel that is not cooperative,
    // is read in full by every block. The cooperative kernel (Grid strategy) and the block-dependent input have none.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task GridUniformAggregateInNonCooperativeKernel_ReportsGSH0100()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/ParallelAggregate/ParallelAggregateWarnings.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // mutant: ParallelAggregateRule reports GSH0100 without checking IsGridUniform (BlockDependentKernel is reported);
        // LambdaBlockIndexKernel (a lambda that reads BlockIndex) gets none either (second review, B1)
        var warning = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.ParallelAggregateNotCooperative.Id);
        var span = warning.Location.GetLineSpan();
        Assert.Equal((12, 115), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Equal((12, 120), (span.EndLinePosition.Line, span.EndLinePosition.Character));
        Assert.Contains("'SharedKernel' is not cooperative", warning.GetMessage());
        Assert.Contains("'AsParallel().Sum()'", warning.GetMessage());
    }
}
