using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_05_UnmanagedBoundariesSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = GpuAnalyzerTestRunner.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_UnboundedPointer_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_UnboundedPointer.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "CS0214" || d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_MallocWarning_ReportsDiagnosticOrWarningAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_MallocWarning.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0014");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_StackallocLoop_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_StackallocLoop.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_Finalizer_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_Finalizer.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_DynamicKeyword_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_DynamicKeyword.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_TypedReference_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_TypedReference.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_ComInteropPInvokeGCHandle_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var path = GetTestCasePath("MEM-05_ComInteropPInvokeGCHandle.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        var compileDiagnostics = compilation.GetDiagnostics();
        var allDiagnostics = diagnostics.AddRange(compileDiagnostics);

        Assert.NotNull(driver);
        Assert.NotEmpty(allDiagnostics);
    }
}
