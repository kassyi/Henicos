namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A struct whose captured primary constructor parameter the CUDA launcher converts member by member is passed
/// through a host mirror that names the hidden field (#495), so it is not reported. The values are checked by
/// <c>PrimaryConstructorTests</c>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class PrimaryConstructorDiagnosticsTests
{
    private const string TestCase = "Diagnostics/PrimaryConstructorCapturedConverted.cu.cs";

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_ConvertedStructWithCapturedParameter_IsNotReportedOnCuda()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cuda");

        // Both kernels pass a converted struct with a captured parameter: by ref, and nested in an array element
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_IdenticalStructWithCapturedParameter_IsNotReportedOnCpu()
    {
        // Vector2 has the host alignment on the CPU dialect, so the struct is copied as it is, hidden field included
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.StructLayoutNotRepresentable.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.StructTransferNotSupportedOnCpu.Id));
    }
}
