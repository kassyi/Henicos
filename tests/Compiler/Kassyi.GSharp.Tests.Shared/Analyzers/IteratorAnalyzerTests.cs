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

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>>
        RunAnalyzersAsync(string relativeTestCasePath)
    {
        var source = RoslynTestUtility.ReadTestCase(relativeTestCasePath);
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider("Cuda"));
        var diagnostics = await compilation
            .WithAnalyzers(s_analyzers, options)
            .GetAnalyzerDiagnosticsAsync();

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
    public async Task Test_IteratorConsumption_SadPath_ReportsGSH0071WithExactSpans()
    {
        var actual = await RunAnalyzersAsync("Analyzers/IteratorConsumption_SadPath.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id, 24, 36, 24, 50),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 26, 16, 26, 31),
                (GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id, 30, 36, 30, 43),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 35, 14, 35, 25),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 37, 24, 37, 56),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 44, 9, 44, 26),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 50, 26, 50, 37),
                (GSharpDiagnostics.ForeachNotSupported.Id, 51, 9, 54, 10),
                (GSharpDiagnostics.IteratorValueMustBeEnumeratedInPlace.Id, 56, 26, 56, 46)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_IteratorBody_SadPath_ReportsGSH0072WithExactSpans()
    {
        var actual = await RunAnalyzersAsync("Analyzers/IteratorBody_SadPath.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 40, 13, 40, 30),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 48, 9, 48, 14),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 55, 9, 55, 26),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 66, 17, 66, 22),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 69, 17, 69, 22),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 77, 9, 77, 16),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 86, 19, 86, 24),
                (GSharpDiagnostics.UnsupportedSyntax.Id, 94, 9, 97, 10),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 96, 13, 96, 18),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 104, 9, 104, 14),
                (GSharpDiagnostics.UnsupportedSyntax.Id, 110, 9, 113, 10),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 112, 13, 112, 18),
                (GSharpDiagnostics.IteratorBodyConstructNotSupported.Id, 122, 13, 122, 30)
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
