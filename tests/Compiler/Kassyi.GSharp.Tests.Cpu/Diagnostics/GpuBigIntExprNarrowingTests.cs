using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Host;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Cpu.Diagnostics;

[Category(TestCategory.Compiler)]
public class GpuBigIntExprNarrowingTests : TranspilerTestBase
{
    private static readonly GpuBigIntExprAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task LocalDeclared_OnlyNodeReadThenDropped_ReportsGsh0005AtDeclarator()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/NodeReadOnlyDropped.cs");

        Assert.Equal(["(8,22)-(8,33)"], GetSortedSpans(diagnostics));
        Assert.Equal(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id, diagnostics[0].Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.Contains("'sum'", diagnostics[0].GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task LocalDeclared_UsingDeclaration_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/UsingDeclarationAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task LocalDeclared_EvaluatedIntoDestination_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/EvaluateCallAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task LocalDeclared_CompiledToGraphExample_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/CompileToGraphExampleAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task LocalDeclared_CopiedToEvaluatedLocal_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/CopiedToEvaluatedLocalAccepted.cs");

        Assert.Empty(diagnostics);
    }

    // A foreach variable is a copy of an element the collection owns; a value put into a tuple or a collection goes where
    // the rule does not follow, so it is not reported (a form the analysis can only suspect is not an error)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task LocalDeclared_LoopVariableOrPassedIntoTupleOrCollection_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/EscapedAndLoopVariableAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task LocalDeclared_ReturnedFromHelper_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/ReturnedFromHelperAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task FieldAssignment_AssignedDirectly_ReportsGsh0005AtAssignment()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/Assignment.cs");

        Assert.Equal(["(8,8)-(8,21)"], GetSortedSpans(diagnostics));
        Assert.Equal(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id, diagnostics[0].Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.Contains("'_field'", diagnostics[0].GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task CyclicCopyLocals_WithoutEvaluation_ReportGsh0005AtDeclarators()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/DiscardedCycleReported.cs");

        Assert.Equal(["(12,22)-(12,30)", "(8,22)-(8,31)", "(9,22)-(9,27)"], GetSortedSpans(diagnostics));
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id, d.Id));
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task LocalDeclared_OperatorOperandReassignTakesOwnershipValue_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/ValidEdgePatternsAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ConditionalNodeAndDisposable_EvaluatedOrDisposed_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/ConditionalNodeAndDisposableAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalNode_OnlyNumWordsRead_ReportsGsh0005AtDeclarators()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/ConditionalNodeReadOnlyDropped.cs");

        Assert.Equal(["(11,23)-(11,35)", "(8,23)-(8,35)"], GetSortedSpans(diagnostics));
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id, d.Id));
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task OutRefPatternAndAlias_Evaluated_ProducesNoDiagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/OutRefPatternAndAliasAccepted.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task OutParamPatternAliasAndDiscard_Dropped_ReportsExactGsh0005Diagnostics()
    {
        var diagnostics = await AnalyzeCaseAsync("Analyzers/GpuBigIntExpr/OutParamAndPatternDropped.cs");

        Assert.Equal(
            [
                "(13,8)-(13,17)",
                "(18,21)-(18,34)",
                "(19,21)-(19,49)",
                "(21,23)-(21,41)",
                "(27,22)-(27,41)",
                "(30,12)-(30,24)",
                "(31,8)-(31,17)"
            ],
            GetSortedSpans(diagnostics));
        Assert.All(diagnostics, static d => Assert.Equal(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id, d.Id));
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var messages = new string[diagnostics.Length];
        for (var i = 0; i < diagnostics.Length; i++)
        {
            messages[i] = diagnostics[i].GetMessage();
        }

        Assert.Contains(messages, static m => m.Contains("'p'", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("'outUnused'", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("'outTypedUnused'", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("'optDropped'", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("'unEvaluated'", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("'dead'", StringComparison.Ordinal));
        Assert.Contains(messages, static m => m.Contains("'_' ", StringComparison.Ordinal));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeCaseAsync(string relativePathToTestCases)
    {
        var source = RoslynTestUtility.ReadTestCase(relativePathToTestCases);
        var compilation = RoslynTestUtility.CreateCompilation(source);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
        return GeneratorTestRunner.GetAnalyzerDiagnosticsAsync(source, s_analyzer);
    }

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
