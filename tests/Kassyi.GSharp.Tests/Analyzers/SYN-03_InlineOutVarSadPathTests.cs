namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_03_InlineOutVarSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN03_DuplicateOutVar_ReportsDiagnosticAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-03_DuplicateOutVar.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // Verification: Roslyn/Generator catches duplicate variable name error (CS0128) without transpiler crash
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error && (d.Id == "CS0128" || d.Id == "CS0136"));
    }
}
