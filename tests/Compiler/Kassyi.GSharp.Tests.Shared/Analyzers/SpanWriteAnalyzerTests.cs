namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Analyzer tests for <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, and <c>System.Array</c>
/// write / slice members (#731): supported members produce no diagnostics, while unmapped members report <c>GSH0003</c>.
/// </summary>
[Category(TestCategory.Compiler)]
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

        // 1-based (startLine, startCol, endLine, endCol) of the unsupported member references in SpanWriteUnsupported.cs;
        // SupportedAllMembersKernel at the bottom of the file must produce zero diagnostics.
        (int StartLine, int StartCol, int EndLine, int EndCol)[] expected =
        [
            (19, 21, 19, 36), // span.IndexOf(1)
            (26, 21, 26, 37), // span.Contains(1)
            (34, 21, 34, 41), // sa.SequenceEqual(sb)
            (41, 9, 41, 27),   // span.Replace(1, 2)
            (48, 20, 48, 34), // span.ToArray()
            (56, 21, 56, 48), // span.GetPinnableReference()
            (62, 20, 62, 51), // data.AsSpan(Index.FromStart(1))
            (69, 20, 69, 37), // data.AsSpan(1..3)
            (76, 9, 76, 34),   // Array.Resize(ref data, 8)
            (82, 21, 82, 43), // Array.IndexOf(data, 1)
            (88, 27, 88, 45), // Array.Empty<int>()
            (95, 9, 95, 31),   // Array.Sort(data, 0, 2)
            (101, 9, 101, 32), // Array.Sort((Array)data)
            (107, 9, 107, 54), // Array.Sort(data, new SpanWriteDescComparer())
            (113, 9, 113, 33), // Array.Copy(src, dst, 2L)
            (119, 9, 119, 32), // Array.Copy(src, dst, 3) (type mismatch: int[] -> long[])
            (125, 9, 125, 38), // Array.Copy(src, 0, dst, 0, 3) (type mismatch: int[] -> long[])
            (131, 9, 131, 27), // src.CopyTo(dst, 0)
            (137, 21, 137, 30), // data.Rank
            (143, 32, 143, 45), // Action<int[]> action = Array.Reverse;
            (150, 32, 150, 56), // Action<int[]> action = MemoryExtensions.Reverse;
        ];

        var actual = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .OrderBy(static d => d.Location.GetLineSpan().StartLinePosition.Line)
            .Select(static d =>
            {
                var span = d.Location.GetLineSpan();
                return (
                    StartLine: span.StartLinePosition.Line + 1,
                    StartCol: span.StartLinePosition.Character + 1,
                    EndLine: span.EndLinePosition.Line + 1,
                    EndCol: span.EndLinePosition.Character + 1);
            })
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }

        Assert.DoesNotContain(diagnostics, static d => d.Location.GetLineSpan().StartLinePosition.Line + 1 >= 155);
    }
}
