using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_23_31_TypeSystemAndStructSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Theory]
    [InlineData("GEN-23_InvalidFixedTargetSadPath.cs", "CS8385")]
    [InlineData("GEN-30_InvalidInlineArrayLengthSadPath.cs", "CS9167")]
    [InlineData("GEN-31_CircularStructSadPath.cs", "CS0523")]
    [Trait("FeatureId", "GEN-23")]
    [Trait("FeatureId", "GEN-30")]
    [Trait("FeatureId", "GEN-31")]
    [Trait("Category", "SadPath")]
    public void Test_GEN23_31_TypeSystemAndStruct_ReportsDiagnostic(string fileName, string expectedErrorId)
    {
        var allDiagnostics = MegaCompilationCache.GetAllDiagnosticsForFile(fileName);

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id == expectedErrorId);
    }
}
