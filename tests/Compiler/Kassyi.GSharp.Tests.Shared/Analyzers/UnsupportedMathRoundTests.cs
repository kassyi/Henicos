namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class UnsupportedMathRoundTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Round_WithDigitsOrMidpointRounding_ReportsGSH0003AtTheMethodAccess()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/UnsupportedMathRoundOverloads.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // 'Math.Round' / 'MathF.Round' member access of the five unsupported overloads, lines 11 to 15 (1-based)
        (int Line, int StartColumn, int EndColumn)[] expected =
        [
            (10, 20, 30),
            (11, 20, 30),
            (12, 20, 30),
            (13, 22, 33),
            (14, 22, 33),
        ];

        Assert.Equal(expected.Length, diagnostics.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diagnostics[i].Id);
            var span = diagnostics[i].Location.GetLineSpan();
            Assert.Equal((expected[i].Line, expected[i].StartColumn), (span.StartLinePosition.Line, span.StartLinePosition.Character));
            Assert.Equal((expected[i].Line, expected[i].EndColumn), (span.EndLinePosition.Line, span.EndLinePosition.Character));
        }
    }
}
