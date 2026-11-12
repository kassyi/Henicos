using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// An element of a constant string in GPU code is the UTF-16 code unit C# reads, with a constant or a run-time index.
/// A constant index outside the string is correct C# that throws where it runs (it may sit in a branch that never
/// runs), so it is not reported; any element of a null constant string, and foreach over one, throws for certain,
/// which is reported (GSH0003) at the access or the source of the loop instead of written.
/// </summary>
[Category(TestCategory.Compiler)]
public class ConstantStringElementDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ConstantStringElement.cu.cs";

    // Line 24 reads an element of a null constant and line 26 loops over it; lines 21 and 25 index outside a string,
    // which is not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConstantStringElement_NullConstant_ReportsGsh0003AtTheAccessAndTheLoopOnly(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 24, 16, 24, 26), (Analyzer, 26, 28, 26, 35)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
        Assert.Equal(
            [
                "The syntax 'Element of a null constant string (it throws NullReferenceException)' is not " +
                "supported in GSharp GPU kernels",
                "The syntax 'foreach over a null constant string (it throws NullReferenceException)' is not " +
                "supported in GSharp GPU kernels",
            ],
            diagnostics
                .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
                .OrderBy(static d => d.StartLine)
                .Select(static d => d.Message));
        Assert.DoesNotContain(diagnostics, static d => d.Id == GSharpDiagnostics.StringNotSupported.Id);
    }
}
