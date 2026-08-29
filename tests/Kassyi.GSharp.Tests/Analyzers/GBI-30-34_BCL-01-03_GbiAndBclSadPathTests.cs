using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GBI_30_34_BCL_01_03_GbiAndBclSadPathTests
{
    private static string GetTestCasePath(string testName, [CallerFilePath] string callerFilePath = "")
    {
        var projectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, ".."));
        return Path.Combine(projectDir, "TestCases", "Analyzers", "GpuKernel", $"{testName}.cu.cs");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAllDiagnosticsFromTestNameAsync(string testName)
    {
        var path = GetTestCasePath(testName);
        var source = await File.ReadAllTextAsync(path);

        var analyzer = new GpuKernelAnalyzer();
        var analyzerDiags = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);

        var (_, compilation, generatorDiags) = RoslynTestUtility.RunGenerator(source);
        var compilationDiags = compilation.GetDiagnostics();

        return analyzerDiags.AddRange(generatorDiags).AddRange(compilationDiags);
    }

    [Fact]
    [Trait("FeatureId", "GBI-30")]
    [Trait("Category", "SadPath")]
    public async Task Test_GBI30_HostPropertyAccess_ReportsDiagnostic()
    {
        var diagnostics = await GetAllDiagnosticsFromTestNameAsync("GBI-30_HostPropertyAccess_InGpuKernel");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    [Trait("FeatureId", "GBI-31")]
    [Trait("Category", "SadPath")]
    public async Task Test_GBI31_DynamicWarpDivergentSync_ReportsDiagnostic()
    {
        var diagnostics = await GetAllDiagnosticsFromTestNameAsync("GBI-31_DynamicWarpDivergentSync_InGpuKernel");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    [Trait("FeatureId", "GBI-34")]
    [Trait("Category", "SadPath")]
    public async Task Test_GBI34_DynamicStringInlinePtx_ReportsDiagnostic()
    {
        var diagnostics = await GetAllDiagnosticsFromTestNameAsync("GBI-34_DynamicStringInlinePtx_InGpuKernel");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral.Id || d.Id == GSharpDiagnostics.StringNotSupported.Id);
    }
}
