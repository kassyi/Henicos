using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Host;
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
        // 1 (AsStackEnumerable) + 24 (Where) + 1 (OfType) + 1 (static Where)
        // + 23 (Select) = 50 stages (threshold is > 50)
        var expr =
            $"StackLinqExtensions.Where(" +
            $"data.AsStackEnumerable(){BuildChain(24, ".Where(x => x > 0)")}.OfType<int>(), " +
            $"x => x > 0){BuildChain(23, ".Select(x => x + 1)")}";

        var (diagnostics, _) = await AnalyzeAsync(BuildTestClassSource(expr));
        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task StackLinqChain_FiftyStagesPassedToStaticWrapper_ProducesNoDiagnostics()
    {
        // 1 (AsStackEnumerable) + 49 (Where) = 50 stages passed directly to user wrapper Helper.Id.
        // Helper.Id returns a StackLinq type but is not a Kassyi.ZeroAlloc.Linq method,
        // so it must not be counted as a 51st stage.
        var whereChain = BuildChain(49, ".Where(x => x > 0)");
        var source = $$"""
            using Kassyi.ZeroAlloc.Linq;

            public static class Helper
            {
                public static StackEnumerable<TE, T> Id<TE, T>(StackEnumerable<TE, T> s)
                    where TE : struct, IStackEnumerator<T>, allows ref struct => s;
            }

            public class TestClass
            {
                public void TestMethod(int[] data)
                {
                    var seq = Helper.Id(data.AsStackEnumerable(){{whereChain}});
                }
            }
            """;

        var (diagnostics, _) = await AnalyzeAsync(source);
        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_FiftyOneStagesWithUserExtension_ReportsSLQ0001()
    {
        var whereChain = BuildChain(50, ".Where(x => x > 0)");
        var source = $$"""
            using Kassyi.ZeroAlloc.Linq;

            public static class UserHelper
            {
                public static void MySink<TE, T>(this StackEnumerable<TE, T> s)
                    where TE : struct, IStackEnumerator<T>, allows ref struct
                {
                }
            }

            public class TestClass
            {
                public void TestMethod(int[] data)
                {
                    data.AsStackEnumerable(){{whereChain}}.MySink();
                }
            }
            """;

        var (diagnostics, _) = await AnalyzeAsync(source);
        var slq = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, slq.Id);
    }

    [Theory]
    [InlineData("OfType")]
    [InlineData("StaticCall")]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_FiftySixStagesWithNonExtensionStep_ReportsSLQ0001(string stepKind)
    {
        // 1 (AsStackEnumerable) + 30 (Where) + 1 (step) + 24 (Select) = 56 stages (> 50)
        var whereChain = BuildChain(30, ".Where(x => x > 0)");
        var selectChain = BuildChain(24, ".Select(x => x + 1)");

        var expr = stepKind switch
        {
            "OfType" =>
                $"data.AsStackEnumerable(){whereChain}.OfType<int>(){selectChain}",
            "StaticCall" =>
                $"StackLinqExtensions.Where(data.AsStackEnumerable(){whereChain}, x => x > 0){selectChain}",
            _ => throw new ArgumentOutOfRangeException(nameof(stepKind))
        };

        var (diagnostics, root) = await AnalyzeAsync(BuildTestClassSource(expr));
        var slq = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, slq.Id);

        var outermostInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        Assert.Equal(outermostInvocation.GetLocation().GetLineSpan().Span, slq.Location.GetLineSpan().Span);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StackLinqChain_ExceedingMaxDepthWithOfTypeOrStaticCall_ReportsSLQ0001()
    {
        var (_, tree, _) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/PendingBugs/StackLinqChainDepthBypassKernels.cu.cs");

        var sumInvocations = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Sum" })
            .OrderBy(inv => inv.SpanStart)
            .ToArray();

        Assert.Equal(2, sumInvocations.Length);

        var diagnostics = await GeneratorTestRunner.TestAnalyzer(
                "Analyzers/PendingBugs/StackLinqChainDepthBypassKernels.cu.cs",
                new StackLinqChainAnalyzer())
            .GetDiagnosticsAsync();

        var slq0001 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StackLinqChainTooDeep.Id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToArray();

        Assert.Equal(2, slq0001.Length);
        Assert.All(slq0001, d => Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, d.Id));
        Assert.Equal(sumInvocations[0].GetLocation().GetLineSpan().Span, slq0001[0].Location.GetLineSpan().Span);
        Assert.Equal(sumInvocations[1].GetLocation().GetLineSpan().Span, slq0001[1].Location.GetLineSpan().Span);
    }

    private static string BuildChain(int stages, string step) =>
        string.Concat(Enumerable.Repeat(step, stages));

    private static string BuildTestClassSource(string chainExpression) =>
        $$"""
        using Kassyi.ZeroAlloc.Linq;

        public class TestClass
        {
            public void TestMethod(int[] data)
            {
                var seq = {{chainExpression}};
            }
        }
        """;

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
