namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class UserDefinedOperatorAnalyzerTests : TranspilerTestBase
{
    private const string UserDefinedOperatorNotSupportedId = "GSH0060";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UserDefinedOperator_ReportsEachUnsupportedOperatorInGpuCode()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/UserDefinedOperator_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == UserDefinedOperatorNotSupportedId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var expectedPairs = new[]
        {
            ("BinaryKernel", "op_Addition"),
            ("BinaryKernel", "op_Multiply"),
            ("CompoundKernel", "op_Addition"),
            ("UnaryKernel", "op_UnaryNegation"),
            ("IncrementKernel", "op_Increment"),
            ("AddInDevice", "op_Addition")
        };

        var actualPairs = reported.Select(d =>
        {
            var enclosingMethod = GetEnclosingMethodName(d);
            var operatorName = GetOperatorName(d);
            return (enclosingMethod, operatorName);
        }).ToArray();

        Assert.Equal(expectedPairs.Order(), actualPairs.Order());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_UserDefinedOperator_AllowsSupportedExpressions()
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

    private static string GetOperatorName(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage();
        var start = message.IndexOf('\'') + 1;
        return message.Substring(start, message.IndexOf('\'', start) - start);
    }
}
