#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Generators;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SharedTestDialectIfBanAnalyzerTests
{
    private const string SharedDirectory = "tests/Compiler/Kassyi.GSharp.Tests.Shared/";

    // Analyzes the test case as if it were the file at the given path
    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string filePath, string testCase)
    {
        var source = RoslynTestUtility.ReadTestCase($"Analyzers/SharedTestDialectIfBan/{testCase}");
        var compilation = RoslynTestUtility.CreateCompilationAt(filePath, source);
        var analyzer = new SharedTestDialectIfBanAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics
            .Where(d => d.Id == SharedTestDialectIfBanAnalyzer.DiagnosticId)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    private static Task<ImmutableArray<Diagnostic>> RunInSharedAsync(string testCase) =>
        RunAnalyzerAsync(SharedDirectory + testCase, testCase);

    // Asserts an HNT0010 error on the symbol at the given line and columns (all zero-based)
    private static void AssertReported(Diagnostic diagnostic, string symbol, int line, int startColumn, int endColumn)
    {
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(SharedTestDialectIfBanAnalyzer.DiagnosticId, diagnostic.Id);
        var lineSpan = diagnostic.Location.GetLineSpan();
        Assert.Equal(line, lineSpan.StartLinePosition.Line);
        Assert.Equal(line, lineSpan.EndLinePosition.Line);
        Assert.Equal(startColumn, lineSpan.StartLinePosition.Character);
        Assert.Equal(endColumn, lineSpan.EndLinePosition.Character);
        Assert.Contains(symbol, diagnostic.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IfGsharpCpu_InShared_ReportsErrorAtSymbol()
    {
        var diagnostics = await RunInSharedAsync("IfCpu.cs");

        AssertReported(Assert.Single(diagnostics), "GSHARP_CPU", line: 0, startColumn: 4, endColumn: 14);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IfGsharpCuda_InShared_ReportsErrorAtSymbol()
    {
        var diagnostics = await RunInSharedAsync("IfCuda.cs");

        AssertReported(Assert.Single(diagnostics), "GSHARP_CUDA", line: 0, startColumn: 4, endColumn: 15);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IfNegatedGsharpCpu_InShared_ReportsErrorAtSymbol()
    {
        var diagnostics = await RunInSharedAsync("IfNotCpu.cs");

        AssertReported(Assert.Single(diagnostics), "GSHARP_CPU", line: 0, startColumn: 5, endColumn: 15);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ElifGsharpCuda_InShared_ReportsErrorAtSymbol()
    {
        var diagnostics = await RunInSharedAsync("ElifCuda.cs");

        AssertReported(Assert.Single(diagnostics), "GSHARP_CUDA", line: 2, startColumn: 6, endColumn: 17);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IfGsharpCpuAndOtherSymbol_InShared_ReportsErrorOnlyAtDialectSymbol()
    {
        var diagnostics = await RunInSharedAsync("IfCpuAndOther.cs");

        AssertReported(Assert.Single(diagnostics), "GSHARP_CPU", line: 0, startColumn: 4, endColumn: 14);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IfParenthesizedBothSymbols_InShared_ReportsErrorForBoth()
    {
        var diagnostics = await RunInSharedAsync("IfCpuOrCuda.cs");

        Assert.Equal(2, diagnostics.Length);
        AssertReported(diagnostics[0], "GSHARP_CPU", line: 0, startColumn: 5, endColumn: 15);
        AssertReported(diagnostics[1], "GSHARP_CUDA", line: 0, startColumn: 19, endColumn: 30);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task IfGsharpCpu_InCpuAssemblyFile_ReportsNothing()
    {
        var diagnostics = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Cpu/CpuTests.cs", "IfCpu.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task IfGsharpCuda_InCudaAssemblyFile_ReportsNothing()
    {
        var diagnostics = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Cuda/CudaTests.cs", "IfCuda.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task OtherPreprocessorSymbols_InShared_ReportsNothing()
    {
        var diagnostics = await RunInSharedAsync("OtherSymbols.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task DialectNamesInCommentsAndStrings_InShared_ReportsNothing()
    {
        var diagnostics = await RunInSharedAsync("CommentsAndStrings.cs");

        Assert.Empty(diagnostics);
    }
}
