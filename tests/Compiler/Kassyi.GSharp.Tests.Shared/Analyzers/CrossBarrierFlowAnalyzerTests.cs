#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// GSH0039 / GSH0041 follow the control flow graph: a local is reported when it is live right after a point where the
/// CPU dialects split the state machine (a barrier, a warp shuffle, a grid sync, a gap of a Gpu.SyncGrid body, a call
/// that suspends), on any path including the back edge of a loop (A-21, #668).
/// </summary>
[Category(TestCategory.Compiler)]
public class CrossBarrierFlowAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CrossBarrierLocals_ReportedOnlyWhenLiveAfterASplit()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierFlowKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var actual = diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 31, 19, 31, 23),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 55, 14, 55, 21),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 83, 19, 83, 23),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 93, 19, 93, 20),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 106, 23, 106, 24),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 132, 14, 132, 21),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 143, 17, 143, 22),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 152, 14, 152, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 162, 14, 162, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 175, 14, 175, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 189, 14, 189, 15),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 234, 19, 234, 23),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 257, 14, 257, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 281, 14, 281, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 305, 14, 305, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 316, 14, 316, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 328, 14, 328, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 350, 14, 350, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 363, 14, 363, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 375, 14, 375, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 399, 14, 399, 15)
            ],
            actual);
    }
}
