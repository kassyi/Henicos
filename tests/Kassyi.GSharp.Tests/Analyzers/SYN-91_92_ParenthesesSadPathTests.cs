namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_91_92_ParenthesesSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-91")]
    [Trait("FeatureId", "SYN-92")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN91_92_InvalidParentheses_ReportsDiagnosticsAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-91_92_InvalidParenthesesSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
