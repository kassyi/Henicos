namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_17_TargetTypedNewSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN17_UnresolvedTargetTypedNew_ReportsDiagnosticAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-17_UnresolvedTargetTypedNew.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // Verification: Target type inference failure reports diagnostic error (CS8754) without transpiler crash
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS8754");
    }
}
