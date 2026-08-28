namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_08_LambdaAndLocalFunctionsSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN08_DelegateEscapeAndRefCapture_ReportsDiagnosticsAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-08_DelegateEscapeSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // Verification: Roslyn / Transpiler / Generator reports diagnostic errors (e.g. GSH0001 or object/delegate creation diagnostics) without compiler crashing
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
