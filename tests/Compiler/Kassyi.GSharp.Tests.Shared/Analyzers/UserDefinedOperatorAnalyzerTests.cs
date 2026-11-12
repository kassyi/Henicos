namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class UserDefinedOperatorAnalyzerTests : TranspilerTestBase
{
    private const string UserDefinedOperatorNotSupportedId = "GSH0060";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task UserDefinedOperator_UnsupportedOperatorsInGpuCode_ReportsEachUnsupportedOperator()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/UserDefinedOperator_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == UserDefinedOperatorNotSupportedId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // (enclosing method, operator token, expression it applies to)
        var expectedPairs = new[]
        {
            ("BinaryKernel", SyntaxKind.PlusToken, SyntaxKind.AddExpression),
            ("BinaryKernel", SyntaxKind.AsteriskToken, SyntaxKind.MultiplyExpression),
            ("CompoundKernel", SyntaxKind.PlusEqualsToken, SyntaxKind.AddAssignmentExpression),
            ("UnaryKernel", SyntaxKind.MinusToken, SyntaxKind.UnaryMinusExpression),
            ("IncrementKernel", SyntaxKind.PlusPlusToken, SyntaxKind.PostIncrementExpression),
            ("SystemNumericsVectorKernel", SyntaxKind.PlusToken, SyntaxKind.AddExpression),
            ("AddInDevice", SyntaxKind.PlusToken, SyntaxKind.AddExpression)
        };

        var actualPairs = reported.Select(d =>
        {
            var operatorToken = GetReportedOperator(d);
            return (GetEnclosingMethodName(d), operatorToken.Kind(), operatorToken.Parent?.Kind() ?? SyntaxKind.None);
        }).ToArray();

        Assert.Equal(expectedPairs.Order(), actualPairs.Order());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task UserDefinedOperator_SupportedArithmeticExpressions_AllowsSupportedExpressions()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/UserDefinedOperator_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == UserDefinedOperatorNotSupportedId);
    }

    private static string GetEnclosingMethodName(Diagnostic diagnostic)
    {
        var node = diagnostic.Location.SourceTree?.GetRoot().FindNode(diagnostic.Location.SourceSpan);
        while (node != null)
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method)
            {
                return method.Identifier.ValueText;
            }
            node = node.Parent;
        }
        return string.Empty;
    }

    // The operator token the diagnostic is reported on; its span must be the diagnostic's span.
    private static SyntaxToken GetReportedOperator(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree
            ?? throw new InvalidOperationException($"{diagnostic.Id} has no source location.");
        var token = tree.GetRoot().FindToken(diagnostic.Location.SourceSpan.Start);
        Assert.Equal(diagnostic.Location.SourceSpan, token.Span);
        return token;
    }
}
