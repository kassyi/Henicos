using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// A call that C# removes, its arguments included, never runs, so the barrier and iterator diagnostics do not look
/// inside it; the twin that C# keeps is still reported, at the same place.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class RemovedCallDiagnosticsTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> s_analyzers =
    [
        new GpuKernelAnalyzer(),
        new BarrierUsageAnalyzer()
    ];

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>>
        GetSpansAsync(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/RemovedCallDiagnostics.cs"));
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(dialect));
        var diagnostics = await compilation
            .WithAnalyzers(s_analyzers, options)
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics
            .Select(static d =>
            {
                var span = d.Location.GetLineSpan().Span;
                return (d.Id, span.Start.Line + 1, span.Start.Character + 1, span.End.Line + 1, span.End.Character + 1);
            })
            .OrderBy(static s => s.Item2)
            .ThenBy(static s => s.Item3)
            .ToList();
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task BarrierDiagnostics_RemovedCalls_ReportOnlyTheKeptTwins(string dialect)
    {
        var actual = await GetSpansAsync(dialect);

        Assert.Equal(
            [
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 26, 14, 26, 36),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 52, 13, 52, 19),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 68, 14, 68, 19),
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 86, 33, 86, 55),
                (GSharpDiagnostics.DivergentDynamicSharedMemory.Id, 107, 21, 107, 34)
            ],
            actual);
    }
}
