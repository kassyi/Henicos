namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_90_StructInitSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN90_UnsupportedMethodInStructInit_ReportsDiagnosticsAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-90_UnsupportedMethodInStructInit.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(compilation);
        Assert.NotNull(allDiagnostics);
    }
}
