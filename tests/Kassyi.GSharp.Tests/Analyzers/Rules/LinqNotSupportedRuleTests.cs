// QA Reference: .agents/skills/qa-testing-guidelines/SKILL.md
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class LinqNotSupportedRuleTests
{
    [Fact]
    [Trait("FeatureId", "GSH-02")]
    public void Validate_GSH02_ReportsDiagnostic_OnQueryExpression()
    {
        // Arrange
        var rule = LinqNotSupportedRule.Instance;
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { var x = from a in list select a; } }");
        var node = tree.GetRoot().DescendantNodes().OfType<QueryExpressionSyntax>().First();
        var (model, method) = TestHelpers.CreateDummyContext();
        
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
