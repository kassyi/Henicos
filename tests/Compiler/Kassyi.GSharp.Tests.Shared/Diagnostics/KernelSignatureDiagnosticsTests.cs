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
    public async Task Test_UnsupportedReturnTypes_ReportGsh0004AtTheMethodName(string dialect)
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
    public async Task Test_InstanceDeviceFunctions_ReportGsh0066AtTheMethodName(string dialect)
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
    public async Task Test_FunctionPointerParameter_ReportsGsh0003AtTheParameterType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxNodes.cu.cs", dialect);

        // Line 44 is a function pointer variable, which the transpiler still reports
        Assert.Equal(
            [
                (Analyzer, 35, 35, 35, 54),
                (DiagnosticOrigin.Generator, 44, 9, 44, 28)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'FunctionPointerType'"));
    }

    // Exactly 4096 bytes (AtLimitKernel), a small struct and a device function are not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_KernelParameterSpace_ReportsGsh0064OnlyOverTheLimit(string dialect)
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
}
