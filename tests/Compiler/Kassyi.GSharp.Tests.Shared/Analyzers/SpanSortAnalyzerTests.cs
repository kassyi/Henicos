using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// The forms of <c>MemoryExtensions.Sort</c> that device code cannot run are reported as GSH0003 at the argument (or the
/// call) that causes them (#625), and nothing else is reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class SpanSortAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Sort_UnsupportedForms_ReportGSH0003AtTheirArgument()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/SpanSort/SpanSortUnsupported.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // (start line, start column, end line, end column), 0-based, and the reason, one kernel each
        (int, int, int, int, string)[] expected =
        [
            (22, 18, 22, 55, MemoryExtensionsTranspilerRule.ComparerNotStruct), // Comparer<int>.Create(...): a class
            (28, 36, 28, 60, MemoryExtensionsTranspilerRule.NullComparison), // default(Comparison<int>)
            (34, 8, 34, 21, MemoryExtensionsTranspilerRule.NoDefaultOrder), // values.Sort() of a struct without IComparable<T>
            (40, 18, 40, 46, MemoryExtensionsTranspilerRule.ComparerNotStruct), // a generic comparer struct
            (46, 8, 46, 55, MemoryExtensionsTranspilerRule.ReorderedArguments), // Sort(items: ..., keys: ...)
            (53, 18, 53, 25, MemoryExtensionsTranspilerRule.ComparisonNotFunction), // a Comparison<int> local
            (59, 18, 59, 38, MemoryExtensionsTranspilerRule.SeveralComparerInterfaces), // IComparer<int> and IComparer<long>
            (65, 8, 65, 19, MemoryExtensionsTranspilerRule.NoDefaultOrder), // nint keys
        ];
        var actual = diagnostics
            .Select(static d => (d.Id, Span: d.Location.GetLineSpan(), Message: d.GetMessage()))
            .OrderBy(static d => d.Span.StartLinePosition.Line)
            .Select(static d => (d.Id, Start: (d.Span.StartLinePosition.Line, d.Span.StartLinePosition.Character),
                End: (d.Span.EndLinePosition.Line, d.Span.EndLinePosition.Character), d.Message))
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            var (startLine, startColumn, endLine, endColumn, reason) = expected[i];
            Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, actual[i].Id);
            Assert.Equal((startLine, startColumn), actual[i].Start);
            Assert.Equal((endLine, endColumn), actual[i].End);
            Assert.Contains(reason, actual[i].Message);
        }
    }
}
