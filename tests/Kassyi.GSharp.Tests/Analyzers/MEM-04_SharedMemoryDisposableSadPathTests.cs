using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_04_SharedMemoryDisposableSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_SharedMemory_WithIDisposableElement_EmitsGSH025()
    {
        var path = GetTestCasePath("MEM-04_SharedMemoryDisposableSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var analyzer = new GpuKernelAnalyzer();
        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);

        diagnostics.AssertContainsError(GSharpDiagnostics.SharedMemoryDisposableNotSupported.Id);

        // Assert that exactly 4 errors were reported corresponding to the 4 kernel methods in the sad path file
        var gsh025Errors = diagnostics.Where(d => d.Id == GSharpDiagnostics.SharedMemoryDisposableNotSupported.Id).ToList();
        Assert.Equal(4, gsh025Errors.Count);
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    public async Task Test_MEM04_SharedMemory_WithNonDisposableElement_DoesNotEmitGSH025()
    {
        var path = GetTestCasePath("MEM-04_SharedMemoryDisposableHappyPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var analyzer = new GpuKernelAnalyzer();
        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);

        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.SharedMemoryDisposableNotSupported.Id);
    }
}
