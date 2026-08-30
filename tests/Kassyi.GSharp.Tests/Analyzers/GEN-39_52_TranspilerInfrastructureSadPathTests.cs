using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_39_52_TranspilerInfrastructureSadPathTests
{
    private static async Task<string> LoadTestCaseSourceAsync()
    {
        var testCasesDir = GpuAnalyzerTestRunner.GetTestCasesDirectory();
        var path = Path.Combine(testCasesDir, "Analyzers", "GEN-39_52_InvalidMappingSadPath.cs");
        return await File.ReadAllTextAsync(path);
    }

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "GEN-39")]
    public async Task Test_GEN39_InvalidBclIntrinsicMapping_DoesNotCrash()
    {
        var source = await LoadTestCaseSourceAsync();

        // Execution of source generator with invalid BCL intrinsic mapping attribute must complete safely without throwing exceptions
        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);

        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        // Ensure compilation/generator didn't crash
    }

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "GEN-52")]
    public async Task Test_GEN52_UnknownAttributesAndInvalidIntrinsics_ReportsDiagnostics()
    {
        var source = await LoadTestCaseSourceAsync();

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(driver);
        // Verification: Roslyn / Transpiler / Generator reports specific diagnostic errors (GSH0001 for object creation) without compiler crashing
        Assert.Contains(allDiagnostics, d => d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }
}

