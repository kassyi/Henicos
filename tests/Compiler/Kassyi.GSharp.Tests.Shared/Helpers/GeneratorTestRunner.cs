#nullable enable
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Runs the G# source generator and diagnostic analyzers on test sources, and starts the fluent assertion builders for
/// them.
/// </summary>
public static class GeneratorTestRunner
{
    /// <summary>Runs a DiagnosticAnalyzer on the given source text and returns reported diagnostics.</summary>
    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source,
        DiagnosticAnalyzer analyzer, string targetDialect = "Cuda")
    {
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var analyzerOptions =
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(targetDialect));
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer), analyzerOptions);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    /// <summary>Executes the GpuKernelGenerator on the given source text and produces the output compilation and diagnostics.</summary>
    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)
        RunGenerator(string source, string targetDialect = "Cuda", Dictionary<string, string>? additionalOptions = null) =>
        RunGenerator(RoslynTestUtility.CreateCompilation(source), targetDialect, additionalOptions);

    /// <summary>Executes the GpuKernelGenerator on the given compilation and produces the output compilation and diagnostics.</summary>
    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)
        RunGenerator(Compilation compilation, string targetDialect = "Cuda", Dictionary<string, string>? additionalOptions = null)
    {
        var generator = new GpuKernelGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(targetDialect, additionalOptions));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }

    /// <summary>Initiates a fluent test assertion builder for Source Generator verification on a test case file.</summary>
    public static GeneratorTestConfig TestGenerator(string relativePathToTestCases,
        [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory(callerFilePath);
        return new GeneratorTestConfig(Path.Combine(testCasesDir, relativePathToTestCases));
    }

    /// <summary>Initiates a fluent test assertion builder for Diagnostic Analyzer verification on a test case file.</summary>
    public static AnalyzerTestConfig TestAnalyzer(string relativePathToTestCases, DiagnosticAnalyzer analyzer,
        [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory(callerFilePath);
        return new AnalyzerTestConfig(Path.Combine(testCasesDir, relativePathToTestCases), analyzer);
    }
}
