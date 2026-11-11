namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class UnsupportedMathRoundTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Round_WithDigitsOrMidpointRounding_ReportsGsh0109AtTheCall()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/UnsupportedMathRoundOverloads.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // The calls of the five overloads the dialect does not map, lines 11 to 15 (1-based)
        (int Line, int StartColumn, int EndColumn)[] expected =
        [
            (10, 20, 44),
            (11, 20, 72),
            (12, 20, 69),
            (13, 22, 49),
            (14, 22, 71),
        ];

        Assert.Equal(expected.Length, diagnostics.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id, diagnostics[i].Id);
            var span = diagnostics[i].Location.GetLineSpan();
            Assert.Equal((expected[i].Line, expected[i].StartColumn), (span.StartLinePosition.Line, span.StartLinePosition.Character));
            Assert.Equal((expected[i].Line, expected[i].EndColumn), (span.EndLinePosition.Line, span.EndLinePosition.Character));
        }
    }
}
