using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class CopyAsyncWaitGroupAnalyzerTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CopyAsyncWaitGroup_NonConstantOrOutOfRange_ReportsGSH0074OnArgument()
    {
        var diagnostics = (await RoslynTestUtility
            .TestAnalyzer("Analyzers/CopyAsyncWaitGroup_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync())
            .Where(static d => d.Id == GSharpDiagnostics.CopyAsyncWaitGroupCountOutOfRange.Id)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToArray();

        // 0 and 7 are accepted; dynamicCount (line 26), -1 (line 27), and 8 (line 28) report GSH0074
        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var span0 = diagnostics[0].Location.GetLineSpan();
        Assert.Equal((25, 31), (span0.StartLinePosition.Line, span0.StartLinePosition.Character));
        Assert.Equal((25, 43), (span0.EndLinePosition.Line, span0.EndLinePosition.Character));

        var span1 = diagnostics[1].Location.GetLineSpan();
        Assert.Equal((26, 31), (span1.StartLinePosition.Line, span1.StartLinePosition.Character));
        Assert.Equal((26, 33), (span1.EndLinePosition.Line, span1.EndLinePosition.Character));

        var span2 = diagnostics[2].Location.GetLineSpan();
        Assert.Equal((27, 31), (span2.StartLinePosition.Line, span2.StartLinePosition.Character));
        Assert.Equal((27, 32), (span2.EndLinePosition.Line, span2.EndLinePosition.Character));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CopyAsync_InvalidDestinationOrNonGlobalSource_ReportsGSH0075OnArgument()
    {
        var diagnostics = (await RoslynTestUtility
            .TestAnalyzer("Analyzers/CopyAsyncWaitGroup_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync())
            .Where(static d => d.Id == GSharpDiagnostics.InvalidCopyAsyncArgument.Id)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToArray();

        // Device function `in int src` (line 10) and kernel `in/implicit input[tid]` (lines 20..21) are accepted;
        // lines 33..38 report GSH0075 on input[tid] + 1, s, x, smem2[tid], local, and output[tid]
        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var span0 = diagnostics[0].Location.GetLineSpan();
        Assert.Equal((32, 37), (span0.StartLinePosition.Line, span0.StartLinePosition.Character));
        Assert.Equal((32, 51), (span0.EndLinePosition.Line, span0.EndLinePosition.Character));
        Assert.Contains("globalSrc", diagnostics[0].GetMessage());
        Assert.Contains("global memory", diagnostics[0].GetMessage());

        var span1 = diagnostics[1].Location.GetLineSpan();
        Assert.Equal((33, 37), (span1.StartLinePosition.Line, span1.StartLinePosition.Character));
        Assert.Equal((33, 38), (span1.EndLinePosition.Line, span1.EndLinePosition.Character));
        Assert.Contains("globalSrc", diagnostics[1].GetMessage());
        Assert.Contains("global memory", diagnostics[1].GetMessage());

        var span2 = diagnostics[2].Location.GetLineSpan();
        Assert.Equal((34, 40), (span2.StartLinePosition.Line, span2.StartLinePosition.Character));
        Assert.Equal((34, 41), (span2.EndLinePosition.Line, span2.EndLinePosition.Character));
        Assert.Contains("globalSrc", diagnostics[2].GetMessage());
        Assert.Contains("global memory", diagnostics[2].GetMessage());

        var span3 = diagnostics[3].Location.GetLineSpan();
        Assert.Equal((35, 40), (span3.StartLinePosition.Line, span3.StartLinePosition.Character));
        Assert.Equal((35, 50), (span3.EndLinePosition.Line, span3.EndLinePosition.Character));
        Assert.Contains("globalSrc", diagnostics[3].GetMessage());
        Assert.Contains("global memory", diagnostics[3].GetMessage());

        var span4 = diagnostics[4].Location.GetLineSpan();
        Assert.Equal((36, 26), (span4.StartLinePosition.Line, span4.StartLinePosition.Character));
        Assert.Equal((36, 31), (span4.EndLinePosition.Line, span4.EndLinePosition.Character));
        Assert.Contains("sharedDst", diagnostics[4].GetMessage());
        Assert.Contains("SharedMemory<T>", diagnostics[4].GetMessage());

        var span5 = diagnostics[5].Location.GetLineSpan();
        Assert.Equal((37, 26), (span5.StartLinePosition.Line, span5.StartLinePosition.Character));
        Assert.Equal((37, 37), (span5.EndLinePosition.Line, span5.EndLinePosition.Character));
        Assert.Contains("sharedDst", diagnostics[5].GetMessage());
        Assert.Contains("SharedMemory<T>", diagnostics[5].GetMessage());
    }
}
