using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>[GpuKernel]</c> method named without a call (a delegate to it would call it from device code), and the
/// combination of delegates (a delegate holds one method in device code) are reported once each (GSH0003), at the form.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelMethodGroupDiagnosticsTests
{
    private const string TestCase = "Diagnostics/KernelMethodGroupAndDelegateCombination.cu.cs";
    private const string DeviceFunctionParameterTestCase = "Diagnostics/DeviceFunctionHeapObjectParameters.cu.cs";

    // Line 23 names the kernel alone, line 24 through its type; line 25 converts a device function, which is fine
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelMethodGroup_ConvertedToDelegate_ReportsGsh0003AtTheName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 23, 31, 23, 43),
                (Analyzer, 24, 24, 24, 61)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "used as a method group"));
    }

    // Lines 34 and 35 combine with += and -=, line 36 with +; line 37 copies a delegate, which is fine. Lines 46 and 47
    // combine with a null operand on the left and with a binary -: the result type decides, not the left operand
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DelegateCombination_CompoundAssignmentAndBinary_ReportsGsh0003AtTheExpression(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 34, 9, 34, 21),
                (Analyzer, 35, 9, 35, 21),
                (Analyzer, 36, 30, 36, 43),
                (Analyzer, 46, 33, 46, 45),
                (Analyzer, 47, 33, 47, 48)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "delegate combination"));
        Assert.Equal(11, diagnostics.Count(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // Every place that writes object or Delegate in a body is reported once, at the type, whatever the value is: a cast of
    // a method group (line 55) or a delegate (line 56), and a variable that a delegate or a method group is assigned to
    // (lines 57 and 58)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task HeapObjectType_DelegateConverted_ReportsGsh0003AtTheType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 55, 22, 55, 28),
                (Analyzer, 56, 28, 56, 36),
                (Analyzer, 57, 9, 57, 15),
                (Analyzer, 58, 9, 58, 17)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "type object, Delegate or MulticastDelegate"));
    }

    // Device function parameters of object, Delegate, MulticastDelegate, or 1D arrays of them (including ref/in) are
    // reported once at the parameter type (GSH0003); the call site Take(5) and TakeInt/TakeSpan produce no diagnostic
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task HeapObjectType_DeviceFunctionParameter_ReportsGsh0003AtTheType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(DeviceFunctionParameterTestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 11, 28, 11, 34),
                (Analyzer, 14, 35, 14, 41),
                (Analyzer, 17, 33, 17, 39),
                (Analyzer, 20, 36, 20, 44),
                (Analyzer, 23, 45, 23, 62),
                (Analyzer, 26, 33, 26, 41)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, GpuCodeConstraints.HeapObjectType));
        Assert.Equal(6, diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id).Count);
    }

    // A kernel parameter of the same types is GSH0112 alone: GSH0003 would be a second report of one parameter
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task HeapObjectType_KernelParameter_ReportsGsh0112OnceAndNoGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(DeviceFunctionParameterTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 42, 46, 42, 54), (Analyzer, 48, 48, 48, 58)],
            diagnostics.Ranges(GSharpDiagnostics.KernelParameterHasNoDeviceRepresentation.Id));
        Assert.Equal(6, diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id).Count);
        Assert.Equal(8, diagnostics.Length);
    }
}
