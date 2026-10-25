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

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>>
        RunAnalyzersAsync(string relativeTestCasePath)
    {
        var diagnostics = await GetDiagnosticsAsync(relativeTestCasePath);

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

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_IteratorShape_SadPath_ReportsGSH0070WithExactSpans()
    {
        var actual = await RunAnalyzersAsync("Analyzers/IteratorShape_SadPath.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 14, 26, 14, 39),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 26, 36, 26, 52),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 35, 31, 35, 57),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 44, 29, 44, 45),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 53, 42, 53, 61),
                (GSharpDiagnostics.IteratorShapeNotSupported.Id, 62, 34, 62, 45)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_IteratorConsumption_SadPath_ReportsGSH0071AndGSH0073WithExactSpans()
    {
        var diagnostics = await GetDiagnosticsAsync("Analyzers/IteratorConsumption_SadPath.cs");
        var actual = await RunAnalyzersAsync("Analyzers/IteratorConsumption_SadPath.cs");

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
    public async Task Test_IteratorBody_SadPath_ReportsGSH0072WithExactSpans()
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
    public async Task Test_Iterator_HappyPath_ReportsNoDiagnostics()
    {
        var actual = await RunAnalyzersAsync("Analyzers/Iterator_HappyPath.cs");

        Assert.Empty(actual);
    }
}
