using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class LocalDisposableOwnershipAnalyzerTests : TranspilerTestBase
{
    private static readonly LocalDisposableOwnershipAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DiscardedAllocation_ReportsGSH0067AtTheStatement()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/LocalOwnership/DiscardedAllocation.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // The expression statements of a class, a discard assignment and a struct; the type without IDisposable is silent
        Assert.Equal(["(24,8)-(24,28)", "(29,8)-(29,32)", "(34,8)-(34,26)"], GetSortedSpans(diagnostics));
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.DiscardedDisposableAllocation.Id, d.Id));
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UndisposedLocal_ReportsGSH0068AtTheDeclarator()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/LocalOwnership/UndisposedLocal.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        // Never used, used without disposal, target-typed new, and a callee that only borrows the resource
        Assert.Equal(
            ["(15,12)-(15,37)", "(20,12)-(20,37)", "(26,22)-(26,33)", "(32,12)-(32,37)"],
            GetSortedSpans(diagnostics));
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.UndisposedDisposableLocal.Id, d.Id));
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(diagnostics, static d => Assert.Contains("'ctx'", d.GetMessage()));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_DisposedOrMovedLocals_HaveNoDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/LocalOwnership/LocalOwnershipValid.cs", s_analyzer)
                               .ReportsNoDiagnosticsAsync();
    }

    // The analyzer reports from concurrent actions, so the order of the diagnostics is not fixed
    private static string[] GetSortedSpans(ImmutableArray<Diagnostic> diagnostics)
    {
        var spans = new string[diagnostics.Length];
        for (var i = 0; i < spans.Length; i++)
        {
            spans[i] = diagnostics[i].Location.GetLineSpan().Span.ToString();
        }

        Array.Sort(spans, StringComparer.Ordinal);
        return spans;
    }
}
