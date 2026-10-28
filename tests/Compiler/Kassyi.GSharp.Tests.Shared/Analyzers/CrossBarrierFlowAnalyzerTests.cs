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
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 30, 19, 30, 23),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 54, 14, 54, 21),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 82, 19, 82, 23),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 92, 19, 92, 20),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 105, 23, 105, 24),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 131, 14, 131, 21),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 142, 17, 142, 22),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 151, 14, 151, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 161, 14, 161, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 174, 14, 174, 15),
                (GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id, 188, 14, 188, 15),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 233, 19, 233, 23)
            ],
            actual);
    }
}
