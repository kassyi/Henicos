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

    // The liveness follows the rule the lowering spills arguments by (SpilledArguments, #850): a span that an earlier
    // argument changes stays in place, so the call reads it again when it resumes
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CrossBarrierSpilledArgument_SpanChangedByAnEarlierArgumentIsReported()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierSpilledArgumentKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var actual = diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .ToList();

        Assert.Equal([(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 27, 59, 27, 60)], actual);
    }

    // A by-value ref struct parameter cannot be kept in the frame either: the CPU dialects pass the original argument
    // again when the state machine resumes, so a change made before a split point is lost (#965)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CrossBarrierRefStructParameters_ReportedOnlyWhenChangedBeforeAndLiveAfterASplit()
    {
        // The case binds: an invalid body builds no control flow graph and would report nothing
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Analyzers/BarrierUsage/CrossBarrierRefStructParameterKernels.cu.cs");
        Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);

        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/CrossBarrierRefStructParameterKernels.cu.cs", s_analyzer)
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
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 32, 38, 32, 39),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 41, 51, 41, 53),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 51, 52, 51, 54),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 72, 54, 72, 55),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 84, 41, 84, 42),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 93, 46, 93, 47),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 102, 50, 102, 56),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 111, 51, 111, 57),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 171, 55, 171, 56),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 179, 55, 179, 56),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 195, 54, 195, 55),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 205, 19, 205, 23),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 213, 50, 213, 56),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 222, 56, 222, 62),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 231, 52, 231, 58),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 240, 60, 240, 66),
                (GSharpDiagnostics.CrossBarrierRefStructProhibited.Id, 249, 53, 249, 59)
            ],
            actual);
    }
}
