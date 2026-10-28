#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Shapes that the barrier uniformity analysis once missed or misjudged: a count that a divergent loop leaves behind,
/// a field of a struct local.
/// </summary>
[Category(TestCategory.Compiler)]
public class UniformityReviewAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ReviewedShapes_AreReported()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/UniformityReviewKernels.cu.cs");

        // 27 / 46: a value counted in a loop whose exit test diverges. 61: a field of a struct local
        Assert.Equal(
            [
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 27, 13, 27, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 46, 13, 46, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 61, 13, 61, 30)
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
