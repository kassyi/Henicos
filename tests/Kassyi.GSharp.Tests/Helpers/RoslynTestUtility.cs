using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Helpers;

public static class RoslynTestUtility
{
    public static string GetTestCasesDirectory([CallerFilePath] string callerFilePath = "")
    {
        var fileDir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(fileDir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }
        var projectDir = Path.GetFullPath(Path.Combine(fileDir, ".."));
        var testCasesDir = Path.Combine(projectDir, "TestCases");
        if (!Directory.Exists(testCasesDir))
        {
            throw new DirectoryNotFoundException($"TestCases directory not found at: {testCasesDir}");
        }
        return testCasesDir;
    }

    public static Compilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
    {
        var compilation = CreateCompilation(source);
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);

        var generator = new Kassyi.GSharp.Compiler.GpuKernelGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp11),
            optionsProvider: null);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }

    public static GeneratorTestConfig TestGenerator(string relativePathToTestCases, [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        return new GeneratorTestConfig(Path.Combine(testCasesDir, relativePathToTestCases));
    }

    public static AnalyzerTestConfig TestAnalyzer(string relativePathToTestCases, DiagnosticAnalyzer analyzer, [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        return new AnalyzerTestConfig(Path.Combine(testCasesDir, relativePathToTestCases), analyzer);
    }
}

public class GeneratorTestConfig
{
    private readonly string _fullPath;

    public GeneratorTestConfig(string fullPath)
    {
        _fullPath = fullPath;
    }

    public async Task ReportsErrorsAsync(params string[] expectedDiagnosticIds)
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(outputCompilation.GetDiagnostics());
        foreach (var id in expectedDiagnosticIds)
        {
            allDiagnostics.AssertContainsError(id);
        }
    }

    public async Task ReportsDiagnosticsAsync(params string[] expectedDiagnosticIds)
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(outputCompilation.GetDiagnostics());
        foreach (var id in expectedDiagnosticIds)
        {
            allDiagnostics.AssertContains(id);
        }
    }

    public async Task<GeneratorDriver> GetDriverAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source);
        return driver;
    }

    public async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        return diagnostics.AddRange(outputCompilation.GetDiagnostics());
    }

    public async Task HasErrorsAsync()
    {
        var allDiagnostics = await GetDiagnosticsAsync();
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    public async Task HasNoErrorsAsync()
    {
        var allDiagnostics = await GetDiagnosticsAsync();
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}

public class AnalyzerTestConfig
{
    private readonly string _fullPath;
    private readonly DiagnosticAnalyzer _analyzer;

    public AnalyzerTestConfig(string fullPath, DiagnosticAnalyzer analyzer)
    {
        _fullPath = fullPath;
        _analyzer = analyzer;
    }

    public async Task ReportsErrorsAsync(params string[] expectedDiagnosticIds)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var id in expectedDiagnosticIds)
        {
            diagnostics.AssertContainsError(id);
        }
    }

    public async Task ReportsDiagnosticsAsync(params string[] expectedDiagnosticIds)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var id in expectedDiagnosticIds)
        {
            diagnostics.AssertContains(id);
        }
    }

    public async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        return await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, _analyzer);
    }

    public async Task HasErrorsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    public async Task HasNoErrorsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
