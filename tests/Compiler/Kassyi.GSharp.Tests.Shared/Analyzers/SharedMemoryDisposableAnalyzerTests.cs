namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class SharedMemoryDisposableAnalyzerTests : TranspilerTestBase
{
    private const string DisposableId = "GSH0069";
    private const string ObjectCreationId = "GSH0001";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DisposableElementType_ReportsGSH0069AtTheCreation()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/SharedMemoryDisposable_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // Only the Handle element is disposable; Plain and int are accepted
        var diagnostic = Assert.Single(diagnostics, static d => d.Id == DisposableId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Handle", diagnostic.GetMessage());

        // 'SharedMemory<Handle> handles = new(64);' is line 25; 'new(64)' spans columns 40 to 47 (1-based)
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal((24, 39), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Equal((24, 46), (span.EndLinePosition.Line, span.EndLinePosition.Character));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NewList_ReportsGSH0001WithTheCreationRange()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/NewList_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        // 'var list = new List<int>();' is line 11; the creation spans columns 20 to 35 (1-based)
        var diagnostic = Assert.Single(diagnostics, static d => d.Id == ObjectCreationId);
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal((10, 19), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Equal((10, 34), (span.EndLinePosition.Line, span.EndLinePosition.Character));
    }
}
