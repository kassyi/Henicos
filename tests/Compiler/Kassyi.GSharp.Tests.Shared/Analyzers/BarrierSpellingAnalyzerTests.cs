#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// The barrier analyzer recognizes the synchronization intrinsics and the inlined macros by symbol, however they are
/// spelled: a <c>using</c> alias, <c>using static</c> or a fully qualified name, and not a user's type of the same name
/// (#503).
/// </summary>
[Category(TestCategory.Compiler)]
public class BarrierSpellingAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SpelledBarriers_ReportedWithExactSpans()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/SyncSpellingAnalyzerKernels.cu.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.ExternalBarrierCallProhibited.Id, 15, 9, 15, 22),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 24, 13, 24, 26),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 37, 13, 37, 28),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 53, 13, 53, 28),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 65, 26, 65, 63),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 78, 13, 78, 23)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_SpelledForUnrollBody_SuspendingCallIsInlined_LookalikeIsGSH0062()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/MacroSpellingSuspendingCallKernels.cu.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, 38, 60, 38, 107)
            ],
            actual);
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath)
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(relativeTestCasePath, s_analyzer).GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }
}
