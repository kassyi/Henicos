using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// An element of a constant string in GPU code is the UTF-16 code unit C# reads, with a constant or a run-time index;
/// a constant index outside the string and any element of a null constant string throw in C# for certain, which is
/// reported (GSH0003) at the element access instead of written.
/// </summary>
[Category(TestCategory.Compiler)]
public class ConstantStringElementDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ConstantStringElement.cu.cs";

    // Line 17 reads elements that exist; line 18 indexes past the end, line 19 a null constant, line 20 before the
    // start
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConstantStringElement_KnownToThrow_ReportsGsh0003AtTheAccess(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 18, 16, 18, 23), (Analyzer, 19, 16, 19, 26), (Analyzer, 20, 16, 20, 25)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
        Assert.Equal(
            [
                "The syntax 'Constant index outside a constant string (it throws IndexOutOfRangeException)' is not " +
                "supported in GSharp GPU kernels",
                "The syntax 'Element of a null constant string (it throws NullReferenceException)' is not " +
                "supported in GSharp GPU kernels",
                "The syntax 'Constant index outside a constant string (it throws IndexOutOfRangeException)' is not " +
                "supported in GSharp GPU kernels",
            ],
            diagnostics
                .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
                .OrderBy(static d => d.StartLine)
                .Select(static d => d.Message));
        Assert.DoesNotContain(diagnostics, static d => d.Id == GSharpDiagnostics.StringNotSupported.Id);
    }
}
