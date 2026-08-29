using System.IO;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_33_NativeStructSadPathTests
{
    private static async Task<string> LoadTestCaseSourceAsync()
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        var path = Path.Combine(testCasesDir, "Analyzers", "GEN-33_UnsupportedStructFieldsSadPath.cs");
        return await File.ReadAllTextAsync(path);
    }

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "GEN-33")]
    public async Task Test_GEN33_StructWithUnsupportedManagedFields_EmitsDiagnostics()
    {
        var source = await LoadTestCaseSourceAsync();

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotNull(driver);
        // Verify Roslyn/Generator diagnostics indicate failure for unsupported struct field types (CS0305, CS0208, GSHxxxx, etc.)
        Assert.NotEmpty(allDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }
}
