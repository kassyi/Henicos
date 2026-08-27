using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class ForEachNotSupportedRuleTests
{
    [Fact]
    [Trait("FeatureId", "GSH-01")]
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
