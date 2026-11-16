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
    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string filePath, string source)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(filePath, source);
        var analyzer = new SharedTestDialectIfBanAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics
            .Where(d => d.Id == SharedTestDialectIfBanAnalyzer.DiagnosticId)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task P1_IfGsharpCpu_ReportsErrorAtSymbol()
    {
        const string source = """
            #if GSHARP_CPU
            public class C {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CaseP1.cs", source);

        var diag = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(SharedTestDialectIfBanAnalyzer.DiagnosticId, diag.Id);
        var lineSpan = diag.Location.GetLineSpan();
        Assert.Equal(0, lineSpan.StartLinePosition.Line);
        Assert.Equal(4, lineSpan.StartLinePosition.Character);
        Assert.Equal(14, lineSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CPU", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task P2_IfGsharpCuda_ReportsErrorAtSymbol()
    {
        const string source = """
            #if GSHARP_CUDA
            public class C {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CaseP2.cs", source);

        var diag = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(SharedTestDialectIfBanAnalyzer.DiagnosticId, diag.Id);
        var lineSpan = diag.Location.GetLineSpan();
        Assert.Equal(0, lineSpan.StartLinePosition.Line);
        Assert.Equal(4, lineSpan.StartLinePosition.Character);
        Assert.Equal(15, lineSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CUDA", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task P3_IfNegatedGsharpCpu_ReportsErrorAtSymbol()
    {
        const string source = """
            #if !GSHARP_CPU
            public class C {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CaseP3.cs", source);

        var diag = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        var lineSpan = diag.Location.GetLineSpan();
        Assert.Equal(0, lineSpan.StartLinePosition.Line);
        Assert.Equal(5, lineSpan.StartLinePosition.Character);
        Assert.Equal(15, lineSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CPU", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task P4_ElifGsharpCuda_ReportsErrorAtSymbol()
    {
        const string source = """
            #if DEBUG
            public class C {}
            #elif GSHARP_CUDA
            public class D {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CaseP4.cs", source);

        var diag = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        var lineSpan = diag.Location.GetLineSpan();
        Assert.Equal(2, lineSpan.StartLinePosition.Line);
        Assert.Equal(6, lineSpan.StartLinePosition.Character);
        Assert.Equal(17, lineSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CUDA", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task P5_IfGsharpCpuAndSomething_ReportsErrorOnlyAtDialectSymbol()
    {
        const string source = """
            #if GSHARP_CPU && SOMETHING
            public class C {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CaseP5.cs", source);

        var diag = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        var lineSpan = diag.Location.GetLineSpan();
        Assert.Equal(0, lineSpan.StartLinePosition.Line);
        Assert.Equal(4, lineSpan.StartLinePosition.Character);
        Assert.Equal(14, lineSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CPU", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task P6_IfParenthesizedBothSymbols_ReportsErrorForBoth()
    {
        const string source = """
            #if (GSHARP_CPU || GSHARP_CUDA)
            public class C {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CaseP6.cs", source);

        Assert.Equal(2, diags.Length);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var first = diags[0];
        var firstSpan = first.Location.GetLineSpan();
        Assert.Equal(0, firstSpan.StartLinePosition.Line);
        Assert.Equal(5, firstSpan.StartLinePosition.Character);
        Assert.Equal(15, firstSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CPU", first.GetMessage());

        var second = diags[1];
        var secondSpan = second.Location.GetLineSpan();
        Assert.Equal(0, secondSpan.StartLinePosition.Line);
        Assert.Equal(19, secondSpan.StartLinePosition.Character);
        Assert.Equal(30, secondSpan.EndLinePosition.Character);
        Assert.Contains("GSHARP_CUDA", second.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task N1_CpuAssemblyFile_ProducesNoDiagnostics()
    {
        const string source = """
            #if GSHARP_CPU
            public class CpuOnlyClass {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Cpu/CpuTests.cs", source);
        Assert.Empty(diags);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task N2_CudaAssemblyFile_ProducesNoDiagnostics()
    {
        const string source = """
            #if GSHARP_CUDA
            public class CudaOnlyClass {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Cuda/CudaTests.cs", source);
        Assert.Empty(diags);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task N3_AllowedPreprocessorSymbolsInShared_ProducesNoDiagnostics()
    {
        const string source = """
            #if DEBUG
            public class Dbg {}
            #elif NET10_0_OR_GREATER || NETCOREAPP
            public class NetCore {}
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/AllowedSymbols.cs", source);
        Assert.Empty(diags);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task N4_CommentsAndStringLiterals_ProducesNoDiagnostics()
    {
        const string source = """
            #if DEBUG // GSHARP_CPU is not used
            public class TextCheck
            {
                public string S = "GSHARP_CUDA";
            }
            #endif
            """;
        var diags = await RunAnalyzerAsync("tests/Compiler/Kassyi.GSharp.Tests.Shared/CommentsAndStrings.cs", source);
        Assert.Empty(diags);
    }
}
