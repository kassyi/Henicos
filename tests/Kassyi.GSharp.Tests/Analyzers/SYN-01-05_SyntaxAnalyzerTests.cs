using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Kassyi.GSharp.Compiler.Diagnostics;

using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_01_05_SyntaxAnalyzerTests
{
    private static string GetTestCasePath(string testName, [CallerFilePath] string callerFilePath = "")
    {
        var projectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, ".."));
        return Path.Combine(projectDir, "TestCases", "Analyzers", "GpuKernel", $"{testName}.cu.cs");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetGeneratorDiagnosticsFromTestNameAsync(string testName)
    {
        var path = GetTestCasePath(testName);
        var source = await File.ReadAllTextAsync(path);
        var (_, _, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        return diagnostics;
    }

    [Fact]
    [Trait("FeatureId", "SYN-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN01_NonExhaustiveSwitch_InGpuKernel_ReportsDiagnostic()
    {
        var path = GetTestCasePath("SYN-01_NonExhaustiveSwitch_InGpuKernel");
        var source = await File.ReadAllTextAsync(path);
        var compilation = Helpers.RoslynTestUtility.CreateCompilation(source);
        var compileDiagnostics = compilation.GetDiagnostics();

        // CS8509: The switch expression does not handle all possible values of its input type (it is not exhaustive).
        Assert.Contains(compileDiagnostics, d => d.Id == "CS8509" || d.Id == "CS8506" || d.Id == "CS8524");
    }

    [Fact]
    [Trait("FeatureId", "SYN-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN04_ReferenceNullCoalescing_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetGeneratorDiagnosticsFromTestNameAsync("SYN-04_ReferenceNullCoalescing_InGpuKernel");
        diagnostics.AssertContainsError(GSharpDiagnostics.StringNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "SYN-09")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN09_AmbiguousDefaultLiteral_ReportsDiagnostic()
    {
        var path = Path.GetFullPath(Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Analyzers", "SYN-09_AmbiguousDefaultLiteral.cs"));
        var source = await File.ReadAllTextAsync(path);
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var compileDiagnostics = compilation.GetDiagnostics();

        // CS8716: There is no target type for the default literal.
        Assert.Contains(compileDiagnostics, d => d.Id == "CS8716" || d.Id == "CS0828");
    }
}
