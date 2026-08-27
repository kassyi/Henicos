// Reference: .agents/skills/qa-testing-guidelines/SKILL.md
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Xunit;
using System.Linq;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

public class GSH_03_ImplicitVarNeedsSuffixRuleTests
{
    private const string ID = nameof(GSH_03_ImplicitVarNeedsSuffixRuleTests);

    public static readonly string GSH03_Sources = $$"""
    using System.Collections.Generic;

    class Dummy_{{ID}} { 
        public void WithoutSuffix() {
            var x = 1;
        }

        public void WithSuffix() {
            var x = 1U;
        }
    }
    """;

    [ModuleInitializer]
    public static void Register() => MegaCompilationCache.RegisterSource(GSH03_Sources);

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    public void Validate_GSH03_ReportsDiagnostic_OnVarWithoutSuffix()
    {
        // Arrange
        var rule = ImplicitVarNeedsSuffixRule.Instance;
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "WithoutSuffix");
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
    [Trait("FeatureId", "GSH-03")]
    public void Validate_GSH03_DoesNotReportDiagnostic_OnVarWithSuffix()
    {
        // Arrange
        var rule = ImplicitVarNeedsSuffixRule.Instance;
        var (model, method) = MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "WithSuffix");
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
