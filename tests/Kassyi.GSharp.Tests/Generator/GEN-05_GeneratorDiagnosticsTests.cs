using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class GEN_05_GeneratorDiagnosticsTests
{
    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public async Task TestSadPathDiagnostics()
    {
        var path = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Generator",
            "GEN-05_SadPathDiagnostics.cu.cs");
        var source = await File.ReadAllTextAsync(path);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source);

        await Verifier.Verify(driver).UseMethodName("TestSadPathDiagnostics");
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var path = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "GEN-06_ComprehensiveSpecKernel.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, _, _) = RoslynTestUtility.RunGenerator(source);

        await Verifier.Verify(driver).UseMethodName("TestComprehensiveSpecKernelGeneratedSource");
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var path = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Generator",
            "GEN-07_UnsupportedFeatures.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, _, _) = RoslynTestUtility.RunGenerator(source);

        await Verifier.Verify(driver).UseMethodName("Test_UnsupportedFeatures_ReportsDiagnostics");
    }
}
