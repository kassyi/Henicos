
namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class GSH_02_LinqNotSupportedRuleTests
:  TranspilerTestBase{


    [Fact]
    [Trait("FeatureId", "GSH-02")]
    public void Validate_GSH02_ReportsDiagnostic_OnQueryExpression()
    {
        // Arrange
        var rule = LinqNotSupportedRule.Instance;
        var (model, method) = Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.GetMethodContext<Dummy_GSH02>("Invalid");
        var node = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes().OfType<QueryExpressionSyntax>().First();
        
        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        // Act
        rule.Analyze(node, ref context);

        // Assert
        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.LinqNotSupported.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }


}

class Dummy_GSH02 { 
    public void Valid(System.Collections.Generic.IEnumerable<int> items) {
        foreach(var item in items) { }
    }

    public void Invalid(System.Collections.Generic.IEnumerable<int> items) {
        var x = from i in items select i;
    }
}
