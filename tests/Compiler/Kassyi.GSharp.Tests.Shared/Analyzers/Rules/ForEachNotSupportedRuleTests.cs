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
        const string source = """
            using System.Collections.Generic;

            public class TestContainer
            {
                public void TestMethod(IEnumerable<int> items)
                {
                    foreach (var item in items) { }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNode = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().First();

        var dummyMethod = compilation.GetTypeByMetadataName("TestContainer")!
            .GetMembers("TestMethod").OfType<IMethodSymbol>().First();

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
        const string source = """
            using System;

            namespace CustomNamespace
            {
                public class List<T>
                {
                    public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
                }
            }

            public class TestContainer
            {
                public void TestMethod(CustomNamespace.List<int> list)
                {
                    foreach (var item in list) { }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNode = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().First();

        var dummyMethod = compilation.GetTypeByMetadataName("TestContainer")!
            .GetMembers("TestMethod").OfType<IMethodSymbol>().First();

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
        const string source = """
            using System;

            public class TestContainer
            {
                public void TestMethod(int[] arr, Span<int> span)
                {
                    foreach (var item in arr) { }
                    foreach (var item in span) { }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var forEachNodes = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().ToList();

        var dummyMethod = compilation.GetTypeByMetadataName("TestContainer")!
            .GetMembers("TestMethod").OfType<IMethodSymbol>().First();

        var reportedDiagnostics = new List<Diagnostic>();
        var context = new ValidationContext(model, dummyMethod, reportedDiagnostics.Add);

        foreach (var forEachNode in forEachNodes)
        {
            ForEachNotSupportedRule.Instance.Analyze(forEachNode, ref context);
        }

        Assert.Empty(reportedDiagnostics);
    }
}
