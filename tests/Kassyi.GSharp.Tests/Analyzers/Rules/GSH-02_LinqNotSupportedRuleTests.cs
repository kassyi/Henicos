using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class GSH_02_LinqNotSupportedRuleTests
{
    private class Dummy
    {
        public void TargetMethod()
        {
            var list = new int[0];
            var x = from a in list select a;
        }
    }

    [Fact]
    [Trait("FeatureId", "GSH-02")]
    public void Validate_GSH02_ReportsDiagnostic_OnQueryExpression()
    {
        // Arrange
        var rule = LinqNotSupportedRule.Instance;
        var (model, method) = Kassyi.GSharp.Tests.Helpers.MegaCompilationCache.GetMethodContext(nameof(GSH_02_LinqNotSupportedRuleTests), nameof(Dummy), nameof(Dummy.TargetMethod));
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
