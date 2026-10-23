using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A syntax kind that no transpiler rule handles reaches <c>DefaultVisit</c>; the analyzer runs the transpiler and
/// reports it as GSH0003 at the node, and the generator reports nothing (S-46).
/// </summary>
[Category(TestCategory.Compiler)]
public class UnsupportedSyntaxKindDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CheckedUncheckedAndWith_ReportGsh0003AtTheNode(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxNodes.cu.cs", dialect);

        Assert.Equal([(Analyzer, 18, 19, 18, 39)], diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'CheckedExpression'"));
        Assert.Equal([(Analyzer, 19, 9, 22, 10)], diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'UncheckedStatement'"));
        Assert.Equal([(Analyzer, 30, 21, 30, 41)], diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'WithExpression'"));
    }

    [Theory]
    [InlineData("Cpu", "'IndexExpression'", 13, 26, 13, 28)]
    [InlineData("Cuda", "'IndexExpression'", 13, 26, 13, 28)]
    [InlineData("Cpu", "'TupleExpression'", 20, 21, 20, 33)]
    [InlineData("Cuda", "'TupleExpression'", 20, 21, 20, 33)]
    [InlineData("Cpu", "'ForEachVariableStatement'", 32, 9, 35, 10)]
    [InlineData("Cuda", "'ForEachVariableStatement'", 32, 9, 35, 10)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SyntaxWithoutARule_ReportsGsh0003AtTheNode(string dialect, string kind, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxKinds.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, kind));
    }

    // A kind that C# 12 added (the spread element of a collection expression) is reported without being listed anywhere
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NewerSyntaxKind_ReportsGsh0003FromTheAnalyzer(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxKinds.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 44, 23, 44, 30)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'SpreadElement'"));
    }
}
