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
        Assert.Equal(9, diagnostics.Count(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // Line 55 casts a method group to object, line 56 a delegate to Delegate: both are heap objects
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DelegateCast_ToObjectOrDelegate_ReportsGsh0003AtTheCast(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 55, 21, 55, 32),
                (Analyzer, 56, 27, 56, 43)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "conversion of a delegate to object"));
    }
}
