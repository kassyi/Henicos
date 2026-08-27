using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class GSH_03_ImplicitVarNeedsSuffixRuleTests
{
    [Fact]
    [Trait("FeatureId", "GSH-03")]
    public void Validate_GSH03_ReportsDiagnostic_OnVarWithoutSuffix()
    {
        // Arrange
        var rule = ImplicitVarNeedsSuffixRule.Instance;
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { var x = 1; } }");
        var node = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        var (model, method) = TestHelpers.CreateDummyContext();
        
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
    [Trait("FeatureId", "GSH-03")]
    public void Validate_GSH03_DoesNotReportDiagnostic_OnVarWithSuffix()
    {
        // Arrange
        var rule = ImplicitVarNeedsSuffixRule.Instance;
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { var x = 1U; } }");
        var node = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        var (model, method) = TestHelpers.CreateDummyContext();
        
        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, method, reportedDiagnostics.Add);

        // Act
        rule.Analyze(node, ref context);

        // Assert
        Assert.Empty(reportedDiagnostics);
    }
}
