using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class GSH_01_ForEachNotSupportedRuleTests
{
    private const string ID = nameof(GSH_01_ForEachNotSupportedRuleTests);

    public static readonly string GSH01_Sources = $$"""
    using System.Collections.Generic;

    class Dummy_{{ID}} { 
        public void M(List<int> list) {
            foreach (var x in list) { }
        }
    }
    """;

    [ModuleInitializer]
    public static void Register() => MegaCompilationCache.RegisterSource("GSH_01_ForEachNotSupportedRuleTests.cs", GSH01_Sources);

    [Fact]
    [Trait("FeatureId", "GSH-01")]
    public void Validate_GSH01_ReportsDiagnostic_OnForEachStatement()
    {
        // Arrange
        var rule = ForEachNotSupportedRule.Instance;
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "M");
        var node = method.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes().OfType<ForEachStatementSyntax>().First();
        
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

