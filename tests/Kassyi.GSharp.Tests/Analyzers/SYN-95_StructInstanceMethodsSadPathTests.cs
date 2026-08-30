using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_95_StructInstanceMethodsSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-95")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN95_UnsupportedClassAndDynamicDispatch_ReportsDiagnosticsAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-95_StructInstanceMethodsSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // Verification: Roslyn / Transpiler / Generator reports diagnostics for object creation, unsupported syntax, or string usage
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0001" || d.Id == "GSH0002" || d.Id == "GSH0003");
    }
}
