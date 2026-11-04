using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers.Rules;

public class ForEachNotSupportedRuleTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Validate_GSH01_ReportsDiagnostic_OnForEachStatement()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/ForEachNotSupported/ForEachOverEnumerable.cs"));
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNode = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().First();

        var containerType = compilation.GetTypeByMetadataName("TestContainer");
        Assert.NotNull(containerType);
        var dummyMethod = containerType.GetMembers("TestMethod").OfType<IMethodSymbol>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, dummyMethod, reportedDiagnostics.Add);

        ForEachNotSupportedRule.Instance.Analyze(forEachNode, ref context);

        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.ForeachNotSupported.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Validate_UserDefinedList_ReportsDiagnostic()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/ForEachNotSupported/ForEachOverUserDefinedList.cs"));
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNode = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().First();

        var containerType = compilation.GetTypeByMetadataName("TestContainer");
        Assert.NotNull(containerType);
        var dummyMethod = containerType.GetMembers("TestMethod").OfType<IMethodSymbol>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, dummyMethod, reportedDiagnostics.Add);

        ForEachNotSupportedRule.Instance.Analyze(forEachNode, ref context);

        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.ForeachNotSupported.Id, diagnostic.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Validate_SystemList_ReportsDiagnostic()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/ForEachNotSupported/ForEachOverSystemList.cs"));
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNode = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().First();

        var containerType = compilation.GetTypeByMetadataName("TestContainer");
        Assert.NotNull(containerType);
        var dummyMethod = containerType.GetMembers("TestMethod").OfType<IMethodSymbol>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, dummyMethod, reportedDiagnostics.Add);

        ForEachNotSupportedRule.Instance.Analyze(forEachNode, ref context);

        var diagnostic = Assert.Single(reportedDiagnostics);
        Assert.Equal(GSharpDiagnostics.ForeachNotSupported.Id, diagnostic.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Validate_ArrayAndSpan_DoesNotReportDiagnostic()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/ForEachNotSupported/ForEachOverArrayAndSpan.cs"));
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNodes = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().ToList();

        var containerType = compilation.GetTypeByMetadataName("TestContainer");
        Assert.NotNull(containerType);
        var dummyMethod = containerType.GetMembers("TestMethod").OfType<IMethodSymbol>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, dummyMethod, reportedDiagnostics.Add);

        foreach (var forEachNode in forEachNodes)
        {
            ForEachNotSupportedRule.Instance.Analyze(forEachNode, ref context);
        }

        Assert.Empty(reportedDiagnostics);
    }
}
