using System;
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
    public async Task Test_SYN95_UnsupportedClass_ReportsGSH0001()
    {
        var source = await File.ReadAllTextAsync(GetTestCasePath("SYN-95_StructInstanceMethodsSadPathKernels.cu.cs"));
        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // KernelWithClassInstanceMethod: 'new SampleClass()'
        var kernelDiagnostics = allDiagnostics.Where(d => d.Location.GetLineSpan().StartLinePosition.Line >= 34 && d.Location.GetLineSpan().StartLinePosition.Line <= 40).ToList();
        Assert.Contains(kernelDiagnostics, d => d.Id == "GSH0001");
    }

    [Fact]
    [Trait("FeatureId", "SYN-95")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN95_UnsupportedDynamicDispatch_ReportsGSH0001()
    {
        var source = await File.ReadAllTextAsync(GetTestCasePath("SYN-95_StructInstanceMethodsSadPathKernels.cu.cs"));
        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // KernelWithDynamicDispatch: 'new DerivedClass()'
        var kernelDiagnostics = allDiagnostics.Where(d => d.Location.GetLineSpan().StartLinePosition.Line >= 42 && d.Location.GetLineSpan().StartLinePosition.Line <= 47).ToList();
        Assert.Contains(kernelDiagnostics, d => d.Id == "GSH0001");
    }

    [Fact]
    [Trait("FeatureId", "SYN-95")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN95_UnsupportedStringAndOverride_ReportsGSH0002()
    {
        var source = await File.ReadAllTextAsync(GetTestCasePath("SYN-95_StructInstanceMethodsSadPathKernels.cu.cs"));
        var (_, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        // KernelWithStructOverride: 'string str = s.ToString();'
        var kernelDiagnostics = allDiagnostics.Where(d => d.Location.GetLineSpan().StartLinePosition.Line >= 49 && d.Location.GetLineSpan().StartLinePosition.Line <= 55).ToList();
        Assert.Contains(kernelDiagnostics, d => d.Id == "GSH0002");
    }
}
