using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports the checks of a kernel or device function signature once, at the declaration; the generator
/// reports none of them (S-46).
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelSignatureDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelReturnType_UnsupportedTypes_ReportsGsh0004AtMethodName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Generator/KernelReturnTypes_SadPath.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 10, 27, 10, 38),
                (Analyzer, 14, 26, 14, 38),
                (Analyzer, 30, 33, 30, 52),
                (Analyzer, 34, 39, 34, 49)
            ],
            diagnostics.Ranges(GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeviceFunctionReturnType_UnsupportedType_ReportsGsh0004WithoutGpuKernelWording(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/DeviceFunctionReturnType_SadPath.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 9, 26, 9, 39)
            ],
            diagnostics.Ranges(GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id));

        var gsh0004 = Assert.Single(diagnostics.Where(static d => d.Id == GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id));
        Assert.Equal(DiagnosticOrigin.Analyzer, gsh0004.Origin);
        Assert.DoesNotContain("[GpuKernel]", gsh0004.Message, StringComparison.Ordinal);
        Assert.Equal(
            "Method 'ManagedResult' returns unsupported type 'string'. Kernels and device functions must return void or an unmanaged blittable type; a device function may also be a device iterator.",
            gsh0004.Message);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeviceFunction_InstanceMethod_ReportsGsh0066AtMethodName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/InstanceDeviceFunction_SadPath.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 10, 16, 10, 21),
                (Analyzer, 16, 16, 16, 19)
            ],
            diagnostics.Ranges(GSharpDiagnostics.InstanceDeviceFunctionNotSupported.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task FunctionPointerType_KernelOrDeviceSignature_ReportsGsh0003AtType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxNodes.cu.cs", dialect);

        // A parameter (line 20) and a variable (line 29, UnsupportedConstructRule)
        Assert.Equal(
            [
                (Analyzer, 20, 35, 20, 54),
                (Analyzer, 29, 9, 29, 28)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'FunctionPointerType'"));
    }

    // Exactly 4096 bytes (AtLimitKernel), a small struct and a device function are not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task KernelParameterSpace_ExceedingLimit_ReportsGsh0064(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/StructSizeDiagnostics.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 65, 24, 65, 39),
                (Analyzer, 71, 24, 71, 37),
                (Analyzer, 78, 23, 78, 35)
            ],
            diagnostics.Ranges(GSharpDiagnostics.KernelParameterSpaceExceeded.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnknownTypeParameter_SyntaxAnalysis_DoesNotCrashWithAd0001(string dialect)
    {
        const string TestCasePath = "Analyzers/KernelUnknownParameter.cs";
        var source = RoslynTestUtility.ReadTestCase(TestCasePath);
        var compilation = RoslynTestUtility.CreateCompilation(source);

        var compilationErrors = compilation.GetDiagnostics();
        Assert.Contains(compilationErrors, static d => d.Id == "CS0246");

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.DoesNotContain(diagnostics, static d => d.Id == "AD0001");
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TypeParameter_SyntaxAnalysis_DoesNotCrashWithAd0001(string dialect)
    {
        const string TestCasePath = "Analyzers/KernelTypeParameter.cs";
        var source = RoslynTestUtility.ReadTestCase(TestCasePath);

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.DoesNotContain(diagnostics, static d => d.Id == "AD0001");
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReservedLaunchParameterNames_ReservedIdentifiers_ReportsGsh0106AndSkipsWrappers(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase("Analyzers/KernelReservedLaunchParameter_SadPath.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var reported = run.Analyzer
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Concat(run.Generator.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d)))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToImmutableArray();

        Assert.Equal(
            [
                (Analyzer, 8, 43, 8, 51),
                (Analyzer, 18, 58, 18, 65),
                (Analyzer, 18, 71, 18, 85)
            ],
            reported.Ranges(GSharpDiagnostics.KernelParameterConflictsWithLaunchParameter.Id));

        var generatedSources = run.Output.SyntaxTrees
            .Where(t => !run.Input.SyntaxTrees.Contains(t))
            .Select(static t => t.ToString())
            .ToList();

        Assert.Contains(generatedSources, static generated => generated.Contains("LaunchPlainKernel("));
        foreach (var generated in generatedSources)
        {
            Assert.DoesNotContain("LaunchGridDimXKernel", generated);
            Assert.DoesNotContain("LaunchStreamAndSharedMemBytesKernel", generated);
        }

        var outputDiagnostics = run.Output.GetDiagnostics();
        Assert.DoesNotContain(outputDiagnostics, static d => d.Id == "CS0100");
    }
}
