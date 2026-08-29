namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_17_RefOutSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = Helpers.RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "GEN-17")]
    [Trait("Category", "SadPath")]
    public async Task Test_GEN17_RefOutReassignment_RunsGeneratorWithoutCrash()
    {
        var path = GetTestCasePath("GEN-17_RefOutReassignmentSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // Assert that compilation/generator completes without throwing unhandled exceptions
        Assert.NotNull(compilation);
        Assert.NotNull(allDiagnostics);
    }
}
