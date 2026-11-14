using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class IteratorAnalyzerTests : TranspilerTestBase
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> s_analyzers =
    [
        new GpuKernelAnalyzer(),
        new BarrierUsageAnalyzer()
    ];

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string relativeTestCasePath)
    {
        var source = RoslynTestUtility.ReadTestCase(relativeTestCasePath);
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider("Cuda"));
        return await compilation
            .WithAnalyzers(s_analyzers, options)
            .GetAnalyzerDiagnosticsAsync();
    }

    private static IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>
        ToSpans(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Select(static d =>
            {
                var span = d.Location.GetLineSpan().Span;
                return (
                    d.Id,
                    span.Start.Line + 1,
                    span.Start.Character + 1,
                    span.End.Line + 1,
                    span.End.Character + 1);
            })
            .OrderBy(static d => d.Item2)
            .ThenBy(static d => d.Item3)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>>
        RunAnalyzersAsync(string relativeTestCasePath)
    {
        var diagnostics = await GetDiagnosticsAsync(relativeTestCasePath);
        return ToSpans(diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IteratorShape_InvalidReturnTypeOrSignature_ReportsGSH0070WithExactSpans()
    {
        var diagnostics = await GetDiagnosticsAsync("Analyzers/IteratorShape_SadPath.cs");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "AD0001");

        var actual = ToSpans(diagnostics);

        Assert.Equal(
            [
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 14, 26, 14, 39),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 26, 36, 26, 52),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 35, 31, 35, 57),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 44, 29, 44, 45),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 53, 42, 53, 61),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 62, 34, 62, 45),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 68, 36, 68, 45),
                (GSharpDiagnostics.StringNotSupported.Id, 78, 9, 78, 12)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IteratorConsumption_InvalidMoveNextOrMultipleEnumerators_ReportsGSH0071AndGSH0073WithExactSpans()
    {
        var diagnostics = await GetDiagnosticsAsync("Analyzers/IteratorConsumption_SadPath.cs");
        var actual = ToSpans(diagnostics);

        Assert.Equal(
            [
                (GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id, 24, 36, 24, 50),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 26, 16, 26, 31),
                (GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id, 30, 36, 30, 43),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 83, 14, 83, 25),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 85, 24, 85, 56),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 92, 9, 92, 26),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 98, 26, 98, 37),
                (GSharpDiagnostics.ForeachNotSupported.Id, 99, 9, 102, 10),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 104, 26, 104, 46),
                (GSharpDiagnostics.IteratorExpansionTooLarge.Id, 111, 9, 114, 10)
            ],
            actual);

        var expansionDiag = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.IteratorExpansionTooLarge.Id);
        Assert.Equal(DiagnosticSeverity.Warning, expansionDiag.Severity);
        Assert.Equal(
            "Expanding the iterator 'SixBySix' at this foreach would copy the loop body 36 times (limit 32)",
            expansionDiag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IteratorBody_UnsupportedStatementsInYieldLoop_ReportsGSH0072WithExactSpans()
    {
        var actual = await RunAnalyzersAsync("Analyzers/IteratorBody_SadPath.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 23, 13, 23, 30),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 31, 9, 31, 26),
                (GSharpDiagnostics.UnsupportedSyntax.Id, 39, 9, 42, 10),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 51, 13, 51, 30),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 61, 13, 61, 16),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 72, 13, 72, 16)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Iterator_ValidUsage_ReportsNoDiagnostics()
    {
        var actual = await RunAnalyzersAsync("Analyzers/Iterator_HappyPath.cs");

        Assert.Empty(actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task IteratorInlineDepth_NoDepthLimit_ReportsOnlyOtherDiagnostic()
    {
        var diagnostics = await GetDiagnosticsAsync("Analyzers/IteratorInlineDepth_SadPath.cs");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "AD0001");

        var actual = ToSpans(diagnostics);

        Assert.Equal(
            [
                (GSharpDiagnostics.StringNotSupported.Id, 159, 9, 159, 12)
            ],
            actual);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task IteratorSharedMemory_CrossFileIteratorWithSharedMemoryDeclaration_ReportsGSH0072WithoutGSH0048(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("SharedMemoryCrossFileIterator_Iterator.cs",
                RoslynTestUtility.ReadTestCase("Analyzers/SharedMemoryCrossFileIterator_Iterator.cs")),
            ("SharedMemoryCrossFileIterator_Kernels.cu.cs",
                RoslynTestUtility.ReadTestCase("Analyzers/SharedMemoryCrossFileIterator_Kernels.cu.cs")));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.DoesNotContain(run.Generator, static d => d.Id == GSharpDiagnostics.InternalCompilerError.Id);
        var gsh0072 = Assert.Single(run.Analyzer, static d => d.Id == GSharpDiagnostics.IteratorBodyConstructNotSupported.Id);
        var span = gsh0072.Location.GetLineSpan().Span;
        Assert.Equal(12, span.Start.Line + 1);
        Assert.Equal(9, span.Start.Character + 1);
        Assert.Equal(12, span.End.Line + 1);
        Assert.Equal(12, span.End.Character + 1);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task IteratorInArgument_CrossFileIteratorPassesConvertedArgument_ReportsNoInternalError(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("InArgumentConversionCrossFileIterator_Iterator.cs",
                RoslynTestUtility.ReadTestCase("Analyzers/InArgumentConversionCrossFileIterator_Iterator.cs")),
            ("InArgumentConversionCrossFileIterator_Kernels.cu.cs",
                RoslynTestUtility.ReadTestCase("Analyzers/InArgumentConversionCrossFileIterator_Kernels.cu.cs")));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.DoesNotContain(run.Generator, static d => d.Id == GSharpDiagnostics.InternalCompilerError.Id);
        Assert.Empty(run.Generator);
    }
}
