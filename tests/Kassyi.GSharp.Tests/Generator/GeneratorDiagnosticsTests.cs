using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class GeneratorDiagnosticsTests
{
    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public Task TestSadPathDiagnostics()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(Helpers.RoslynTestUtility.GetTestCasesDirectory(), "Generator", "GEN-05_SadPathDiagnostics.cu.cs"));
        var (_, _, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var gsharpDiagnostics = diagnostics.Where(d => d.Id.StartsWith("GSH"));
        return Verifier.Verify(gsharpDiagnostics);
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(Helpers.RoslynTestUtility.GetTestCasesDirectory(), "GEN-06_ComprehensiveSpecKernel.cu.cs"));

        var (driver, compilation, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);

        var runResult = driver.GetRunResult();
        var sb = new System.Text.StringBuilder();
        foreach (var tree in runResult.GeneratedTrees)
        {
            sb.AppendLine("TREE: " + tree.FilePath);
            sb.AppendLine(tree.GetText().ToString());
        }

        var compDiagnostics = compilation.GetDiagnostics();
        foreach (var result in runResult.Results)
        {
            if (result.Exception != null)
            {
                sb.AppendLine("GENERATOR EXCEPTION: " + result.Exception.ToString());
            }
        }

        if (sb.Length == 0)
        {
            sb.AppendLine("NO CODE GENERATED.");
            sb.AppendLine("Generator Diagnostics:");
            sb.AppendLine(string.Join("\n", diagnostics));
            sb.AppendLine("Compilation Diagnostics:");
            sb.AppendLine(string.Join("\n", compDiagnostics));
        }

        var generatedCode = sb.ToString().Replace("\r\n", "\n");
        return Verifier.Verify(generatedCode);
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public void Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(Helpers.RoslynTestUtility.GetTestCasesDirectory(), "Generator", "GEN-07_UnsupportedFeatures.cu.cs"));
        var (_, _, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        var gsharpDiagnostics = diagnostics.Where(d => d.Id.StartsWith("GSH")).ToList();

        Assert.NotEmpty(gsharpDiagnostics);
        Assert.True(gsharpDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }
}
