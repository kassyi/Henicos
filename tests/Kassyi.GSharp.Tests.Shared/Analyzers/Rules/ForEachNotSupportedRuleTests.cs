
namespace Kassyi.GSharp.Tests.Shared.Analyzers.Rules;

public class ForEachNotSupportedRuleTests
:  TranspilerTestBase{
    [Fact]
    public void Validate_GSH01_ReportsDiagnostic_OnForEachStatement()
    {
        // Arrange
        var rule = ForEachNotSupportedRule.Instance;
        var node = (ForEachStatementSyntax)SyntaxFactory.ParseStatement("foreach (var x in list) { }");
        var (model, method) = TestHelpers.CreateDummyContext();
        
        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        // Act
        rule.Analyze(node, ref context);

        // Assert
        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.ForeachNotSupported.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
