#nullable enable
#pragma warning disable RS0030 // Do not use banned APIs
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>Base fluent assertion helper for Roslyn Generator and Diagnostic Analyzer testing against source files.</summary>
public abstract class DiagnosticTestConfigBase<TSelf>
    where TSelf : DiagnosticTestConfigBase<TSelf>
{
    protected readonly string FullPath;
    public string TargetDialect { get; private set; } = "Cuda";

    protected DiagnosticTestConfigBase(string fullPath)
    {
        FullPath = fullPath;
    }

    public TSelf WithDialect(string dialect)
    {
        TargetDialect = dialect;
        return (TSelf)this;
    }

    /// <summary>Retrieves all diagnostics produced by the target compilation tool.</summary>
    public abstract Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync();

    /// <summary>Asserts that running the tool reports all specified diagnostic error IDs.</summary>
    public async Task ReportsErrorsAsync(params string[] expectedDiagnosticIds)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var id in expectedDiagnosticIds)
        {
            diagnostics.AssertContainsError(id);
        }
    }

    /// <summary>Asserts that running the tool reports all specified diagnostic error descriptors.</summary>
    public async Task ReportsErrorsAsync(params DiagnosticDescriptor[] expectedDescriptors)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var descriptor in expectedDescriptors)
        {
            diagnostics.AssertContainsError(descriptor);
        }
    }

    /// <summary>Asserts that running the tool reports all specified diagnostic IDs regardless of severity.</summary>
    public async Task ReportsDiagnosticsAsync(params string[] expectedDiagnosticIds)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var id in expectedDiagnosticIds)
        {
            diagnostics.AssertContains(id);
        }
    }

    /// <summary>Asserts that running the tool reports all specified diagnostic descriptors regardless of severity.</summary>
    public async Task ReportsDiagnosticsAsync(params DiagnosticDescriptor[] expectedDescriptors)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var descriptor in expectedDescriptors)
        {
            diagnostics.AssertContains(descriptor);
        }
    }

    /// <summary>Asserts that the tool produces at least one error-level diagnostic.</summary>
    public async Task HasErrorsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Asserts that the tool produces zero error-level diagnostics.</summary>
    public async Task HasNoErrorsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}

/// <summary>Fluent assertion helper for Source Generator testing against source files.</summary>
public sealed class GeneratorTestConfig : DiagnosticTestConfigBase<GeneratorTestConfig>
{
    public GeneratorTestConfig(string fullPath) : base(fullPath)
    {
    }

    /// <summary>Runs the generator and returns the underlying GeneratorDriver.</summary>
    public async Task<GeneratorDriver> GetDriverAsync()
    {
        var source = await File.ReadAllTextAsync(FullPath);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, TargetDialect);
        return driver;
    }

    /// <inheritdoc />
    public override async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(FullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source, TargetDialect);
        return diagnostics.AddRange(outputCompilation.GetDiagnostics());
    }
}

/// <summary>Fluent assertion helper for Roslyn Diagnostic Analyzer testing against source files.</summary>
public sealed class AnalyzerTestConfig : DiagnosticTestConfigBase<AnalyzerTestConfig>
{
    private readonly DiagnosticAnalyzer _analyzer;

    public AnalyzerTestConfig(string fullPath, DiagnosticAnalyzer analyzer) : base(fullPath)
    {
        _analyzer = analyzer;
    }

    /// <inheritdoc />
    public override async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(FullPath);
        return await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, _analyzer, TargetDialect);
    }

    /// <summary>Asserts that running the analyzer reports zero diagnostics.</summary>
    public async Task ReportsNoDiagnosticsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.Empty(diagnostics);
    }
}

internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; }

    public TestAnalyzerConfigOptionsProvider(string targetDialect = "Cuda",
        Dictionary<string, string>? additionalOptions = null)
    {
        GlobalOptions = new TestAnalyzerConfigOptions(targetDialect, additionalOptions);
    }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

internal sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly string _targetDialect;
    private readonly Dictionary<string, string>? _additionalOptions;

    public TestAnalyzerConfigOptions(string targetDialect, Dictionary<string, string>? additionalOptions)
    {
        _targetDialect = targetDialect;
        _additionalOptions = additionalOptions;
    }

#pragma warning disable CS8765
    public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
    {
        if (key == "build_property.GSharpTargetDialect")
        {
            value = _targetDialect;
            return true;
        }

        if (_additionalOptions != null && _additionalOptions.TryGetValue(key, out value))
        {
            return true;
        }

        // The snapshots read the C++ through the Source constants, which the generator emits only on request.
        if (key == "build_property.GSharpEmitSource")
        {
            value = "true";
            return true;
        }

        value = null;
        return false;
    }
}
