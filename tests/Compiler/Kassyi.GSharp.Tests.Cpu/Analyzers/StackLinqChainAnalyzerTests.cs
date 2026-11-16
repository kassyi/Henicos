using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StackLinqChainAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task StackLinqChain_ShortChain_ProducesNoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "StackLinqChain/ShortChain_HappyPath.cs",
            new StackLinqChainAnalyzer(),
            nameof(StackLinqChain_ShortChain_ProducesNoDiagnostics));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_LongChainExceedingLimit_ReportsError()
    {
        await VerifyAnalyzerFromFileAsync(
            "StackLinqChain/LongChain_SadPath.cs",
            new StackLinqChainAnalyzer(),
            nameof(StackLinqChain_LongChainExceedingLimit_ReportsError));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task StackLinqChain_ExactlyFiftyStages_ProducesNoDiagnostics()
    {
        // 1 (AsStackEnumerable) + 24 (Where) + 1 (OfType) + 1 (static Where) + 23 (Select) = 50 stages (threshold is > 50)
        var whereChain = string.Concat(Enumerable.Repeat(".Where(x => x > 0)", 24));
        var selectChain = string.Concat(Enumerable.Repeat(".Select(x => x + 1)", 23));
        var source = $$"""
            using Kassyi.ZeroAlloc.Linq;

            public class TestClass
            {
                public void TestMethod(int[] data)
                {
                    var seq = StackLinqExtensions.Where(
                        data.AsStackEnumerable()
                            {{whereChain}}
                            .OfType<int>(),
                        x => x > 0)
                        {{selectChain}};
                }
            }
            """;

        var (diagnostics, _) = await AnalyzeAsync(source);
        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_FiftySixStagesWithOfType_ReportsSLQ0001()
    {
        // 1 (AsStackEnumerable) + 30 (Where) + 1 (OfType) + 24 (Select) = 56 stages (> 50)
        var whereChain = string.Concat(Enumerable.Repeat(".Where(x => x > 0)", 30));
        var selectChain = string.Concat(Enumerable.Repeat(".Select(x => x + 1)", 24));
        var source = $$"""
            using Kassyi.ZeroAlloc.Linq;

            public class TestClass
            {
                public void TestMethod(int[] data)
                {
                    var seq = data.AsStackEnumerable()
                        {{whereChain}}
                        .OfType<int>()
                        {{selectChain}};
                }
            }
            """;

        var (diagnostics, root) = await AnalyzeAsync(source);
        var slq = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, slq.Id);

        var outermostInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        Assert.Equal(outermostInvocation.GetLocation().GetLineSpan().Span, slq.Location.GetLineSpan().Span);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_FiftySixStagesWithStaticCall_ReportsSLQ0001()
    {
        // 1 (AsStackEnumerable) + 30 (Where) + 1 (static Where) + 24 (Select) = 56 stages (> 50)
        var whereChain = string.Concat(Enumerable.Repeat(".Where(x => x > 0)", 30));
        var selectChain = string.Concat(Enumerable.Repeat(".Select(x => x + 1)", 24));
        var source = $$"""
            using Kassyi.ZeroAlloc.Linq;

            public class TestClass
            {
                public void TestMethod(int[] data)
                {
                    var seq = StackLinqExtensions.Where(
                        data.AsStackEnumerable()
                            {{whereChain}},
                        x => x > 0)
                        {{selectChain}};
                }
            }
            """;

        var (diagnostics, root) = await AnalyzeAsync(source);
        var slq = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, slq.Id);

        var outermostInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        Assert.Equal(outermostInvocation.GetLocation().GetLineSpan().Span, slq.Location.GetLineSpan().Span);
    }

    private static async Task<(ImmutableArray<Diagnostic> Diagnostics, SyntaxNode Root)> AnalyzeAsync(string source)
    {
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var analyzer = new StackLinqChainAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var slq0001 = allDiagnostics
            .Where(d => d.Id == GSharpDiagnostics.StackLinqChainTooDeep.Id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();
        return (slq0001, root);
    }
}
