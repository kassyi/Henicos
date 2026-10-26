namespace Kassyi.GSharp.Tests.Shared.Analyzers.Rules;

[Category(TestCategory.Compiler)]
public class ImplicitVarNeedsSuffixRuleTests
:  TranspilerTestBase{
    [Fact]
    public void Validate_GSH03_ReportsDiagnostic_OnVarWithoutSuffix()
    {
        // Arrange
        var rule = ImplicitVarNeedsSuffixRule.Instance;
        var (model, method) = RoslynTestUtility.GetLocalMethodContext(nameof(Dummy_GSH03), "WithoutSuffix");
        var node = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        // Act
        rule.Analyze(node, ref context);

        // Assert
        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.ImplicitVarNeedsSuffix.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Validate_GSH03_DoesNotReportDiagnostic_OnVarWithSuffix()
    {
        // Arrange
        var rule = ImplicitVarNeedsSuffixRule.Instance;
        var (model, method) = RoslynTestUtility.GetLocalMethodContext(nameof(Dummy_GSH03), "WithSuffix");
        var node = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        // Act
        rule.Analyze(node, ref context);

        // Assert
        Assert.Empty(reportedDiagnostics);
    }
}

class Dummy_GSH03
{
    public void WithoutSuffix()
    {
        var x = 1; _ = x;
    }

    public void WithSuffix()
    {
        var x = 1U; _ = x;
    }
}

