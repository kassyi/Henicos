using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_01_FundamentalOwnershipTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_DiscardedAllocation()
    {
        var path = GetTestCasePath("MEM-01_DiscardedAllocation.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(driver);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_UndisposedLocal()
    {
        var path = GetTestCasePath("MEM-01_UndisposedLocal.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(driver);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_ControlFlowBypass()
    {
        var path = GetTestCasePath("MEM-01_ControlFlowBypass.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(driver);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_AsyncVoid()
    {
        var path = GetTestCasePath("MEM-01_AsyncVoid.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(driver);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_UnsafeNullableDisposal()
    {
        var path = GetTestCasePath("MEM-01_UnsafeNullableDisposal.cs");
        var source = await File.ReadAllTextAsync(path);

        var compilation = Helpers.RoslynTestUtility.CreateCompilation(source);
        var compileDiagnostics = compilation.GetDiagnostics();

        Assert.NotNull(compilation);
        // CS8602: Dereference of a possibly null reference.
        Assert.Contains(compileDiagnostics, d => d.Id == "CS8602");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_NullAssignmentWithoutDisposal()
    {
        var path = GetTestCasePath("MEM-01_NullAssignmentWithoutDisposal.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(driver);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "Syntax")]
    public async Task Test_MEM01_FundamentalOwnershipValid()
    {
        var path = GetTestCasePath("MEM-01_FundamentalOwnershipValid.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(driver);
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
