namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_08_LambdaAndLocalFunctionsSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = GpuAnalyzerTestRunner.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN08_DelegateEscapeAndRefCapture_ReportsDiagnosticsAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-08_DelegateEscapeSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // Verification: Roslyn / Transpiler / Generator reports specific diagnostic errors without compiler crashing
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0001"); // Reference type object creation
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0003"); // Unsupported syntax (e.g. delegate escaping assignment)
    }
}

