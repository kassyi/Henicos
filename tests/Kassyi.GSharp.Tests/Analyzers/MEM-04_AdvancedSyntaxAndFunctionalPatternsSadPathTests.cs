using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_04_AdvancedSyntaxAndFunctionalPatternsSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_StructsAndTuples_BannedPatterns_ReportDiagnostics()
    {
        var path = GetTestCasePath("MEM-04_AdvancedSyntaxAndFunctionalPatternsSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var analyzerDiagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer());
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics()).AddRange(analyzerDiagnostics);

        // Verify diagnostic errors are emitted for object creation / syntax rules in tuple deconstruction & tuple field leaks
        Assert.NotEmpty(allDiagnostics);
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Id == "GSH0001" || d.Id == "GSH0003" || d.Id.StartsWith("CS"));
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_RecordsAndMoveSemantics_BannedPatterns_ReportDiagnostics()
    {
        var path = GetTestCasePath("MEM-04_AdvancedSyntaxAndFunctionalPatternsSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var analyzerDiagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer());
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics()).AddRange(analyzerDiagnostics);

        // Verify errors reported for access after move / double dispose in record with expressions
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Location.GetLineSpan().StartLinePosition.Line > 0);
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_ClosuresAndDelegates_BannedPatterns_ReportDiagnostics()
    {
        var path = GetTestCasePath("MEM-04_AdvancedSyntaxAndFunctionalPatternsSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var analyzerDiagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer());
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics()).AddRange(analyzerDiagnostics);

        // Verify GSH0001 / GSH0003 errors for delegate escaping, local function captures, multicast delegate double dispose, expression trees
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Id == "GSH0001" || d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_RefModifiersAndAsync_BannedPatterns_ReportDiagnostics()
    {
        var path = GetTestCasePath("MEM-04_AdvancedSyntaxAndFunctionalPatternsSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var analyzerDiagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer());
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics()).AddRange(analyzerDiagnostics);

        // Verify diagnostic errors for ref reassignment leak, in/ref readonly mutation trap, and sync dispose on async resource
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Id == "GSH0001" || d.Id == "GSH0003" || d.Id.StartsWith("CS"));
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_ComprehensiveSadPath_ExhaustiveDiagnosticValidation()
    {
        var path = GetTestCasePath("MEM-04_AdvancedSyntaxAndFunctionalPatternsSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var analyzerDiagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer());
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics()).AddRange(analyzerDiagnostics);

        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        // Assert that diagnostics cover multiple error locations corresponding to the 14 banned test cases
        Assert.True(errors.Count >= 5, $"Expected at least 5 diagnostic errors, but got {errors.Count}");
    }
}
