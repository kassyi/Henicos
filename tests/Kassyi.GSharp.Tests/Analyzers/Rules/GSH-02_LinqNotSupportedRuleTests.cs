
namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class GSH_02_LinqNotSupportedRuleTests
{
    private const string ID = nameof(GSH_02_LinqNotSupportedRuleTests);

    public static readonly string GSH02_Sources = $$"""
    using System.Linq;
    using System.Collections.Generic;

    class Dummy_{{ID}} { 
        public void Valid(IEnumerable<int> items) {
            foreach(var item in items) { }
        }

        public void Invalid(IEnumerable<int> items) {
            var x = from i in items select i;
        }
    }
    """;

    [ModuleInitializer]
    public static void Register() => Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.RegisterSource("GSH-02_LinqNotSupportedRuleTests.cs", GSH02_Sources);

    [Fact]
    [Trait("FeatureId", "GSH-02")]
    public void Validate_GSH02_ReportsDiagnostic_OnQueryExpression()
    {
        // Arrange
        var rule = LinqNotSupportedRule.Instance;
        var (model, method) = Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "Invalid");
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

