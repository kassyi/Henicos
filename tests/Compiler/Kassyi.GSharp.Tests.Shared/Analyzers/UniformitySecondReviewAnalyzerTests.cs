#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Shapes that the second review of the barrier uniformity analysis (#772) found misjudged: exit-only guards followed by
/// uniform control flow, the discard arm of an exhaustive switch expression.
/// </summary>
[Category(TestCategory.Compiler)]
public class UniformitySecondReviewAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_GuardThenUniformFlow_IsExitOnly()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/GuardThenUniformKernels.cu.cs");

        // 14 / 32 / 51 / 68: guards whose threads only leave, before a uniform if, a uniform loop and a uniform if/else
        // with barriers (GSH0084 at the guard, nothing at the barriers). 91: a barrier under a divergent if, whose
        // skipping threads wait at the barrier of a later uniform if. 115: a barrier after a guard that continues a
        // uniform loop, whose threads wait at the barrier of the next iteration
        Assert.Equal(
            [
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 14, 13, 14, 35),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 32, 13, 32, 35),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 51, 13, 51, 35),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 68, 13, 68, 35),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 91, 13, 91, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 115, 17, 115, 34)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_ExhaustiveSwitchExpression_DoesNotDiverge()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/SwitchExpressionBarrierKernels.cu.cs");

        // 22: the barrier under 'v == 1', where v comes from a switch on the thread index. The barriers after it (25) and
        // after a switch on a uniform value (39) are reached by every thread: the discard arm always matches
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 22, 13, 22, 30)
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
