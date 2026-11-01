namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Analyzer tests for <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, and <c>System.Array</c>
/// write / slice members (#731): supported members produce no diagnostics, while unmapped members report <c>GSH0003</c>.
/// </summary>
[Category(TestCategory.Compiler)]
[Trait("Issue", "731")]
public class SpanWriteAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SpanWrite_UnsupportedForms_ReportGSH0003AndSupportedFormsProduceNone()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/SpanWrite/SpanWriteUnsupported.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // 1-based line numbers of the unsupported member references in SpanWriteUnsupported.cs;
        // SupportedAllMembersKernel at the bottom of the file must produce zero diagnostics.
        int[] expectedLines =
        [
            19,  // span.IndexOf(1)
            26,  // span.Contains(1)
            34,  // sa.SequenceEqual(sb)
            41,  // span.Replace(1, 2)
            48,  // span.ToArray()
            56,  // span.GetPinnableReference()
            62,  // data.AsSpan(Index.FromStart(1))
            69,  // data.AsSpan(1..3)
            76,  // Array.Resize(ref data, 8)
            82,  // Array.IndexOf(data, 1)
            88,  // Array.Empty<int>()
            95,  // Array.Sort(data, 0, 2)
            101, // Array.Sort((Array)data)
            107, // Array.Sort(data, new SpanWriteDescComparer())
            113, // Array.Copy(src, dst, 2L)
            119, // src.CopyTo(dst, 0)
            125, // data.Rank
            131, // Action<int[]> action = Array.Reverse;
            138, // Action<int[]> action = MemoryExtensions.Reverse;
        ];

        var gsh0003Lines = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .Select(static d => d.Location.GetLineSpan().StartLinePosition.Line + 1)
            .Distinct()
            .OrderBy(static line => line)
            .ToArray();

        Assert.Equal(expectedLines, gsh0003Lines);
        Assert.DoesNotContain(diagnostics, static d => d.Location.GetLineSpan().StartLinePosition.Line + 1 >= 143);
    }
}
