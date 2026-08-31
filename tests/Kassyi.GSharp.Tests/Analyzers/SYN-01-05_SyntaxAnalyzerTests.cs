using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_01_05_SyntaxAnalyzerTests
{
    [Fact]
    [Trait("FeatureId", "SYN-01")]
    [Trait("Category", "SadPath")]
    public void Test_SYN01_NonExhaustiveSwitch_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetCompilerDiagnosticsForFile("SYN-01_NonExhaustiveSwitch_InGpuKernel.cu.cs");
        Assert.Contains(diagnostics, d => d.Id == "CS8509" || d.Id == "CS8506" || d.Id == "CS8524");
    }

    [Fact]
    [Trait("FeatureId", "SYN-04")]
    [Trait("Category", "SadPath")]
    public void Test_SYN04_ReferenceNullCoalescing_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("SYN-04_ReferenceNullCoalescing_InGpuKernel.cu.cs");
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

