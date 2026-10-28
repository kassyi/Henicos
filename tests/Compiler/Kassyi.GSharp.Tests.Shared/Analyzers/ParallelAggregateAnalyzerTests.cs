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

        // (line, start column, end column) of the terminal call, 0-based, and the reason; SharedKernel (line 12) has none,
        // and so has DeviceFunctionSelectorKernel (the last one: a selector that passes its element to a device function)
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
            (82, 107, 112, ParallelAggregateFallback.ThreadVariantInput),
            (88, 100, 105, ParallelAggregateFallback.ThreadVariantInput),
            (97, 93, 98, ParallelAggregateFallback.ThreadVariantInput),
            // A switch arm and a for incrementor that only some threads reach (B2)
            // mutant: ParallelAggregatePlanner.IsDivergent with KernelBarrierValidator.GetDivergingCondition only
            (104, 98, 103, ParallelAggregateFallback.DivergentCall),
            (111, 98, 103, ParallelAggregateFallback.DivergentCall),
            // A variant break / continue whose loop holds the call, and a variant return after the call in a loop (M2)
            (125, 55, 60, ParallelAggregateFallback.EarlyExit),
            (146, 55, 60, ParallelAggregateFallback.EarlyExit),
            (157, 75, 80, ParallelAggregateFallback.EarlyExit),
        ];
        var actual = diagnostics
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
}
