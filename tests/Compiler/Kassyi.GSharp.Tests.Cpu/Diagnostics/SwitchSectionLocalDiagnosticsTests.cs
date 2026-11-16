namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A local that one switch section declares and another uses is declared before the first case label in the C++ (#795).
/// An out variable of a ref struct that is not a view of an array cannot be declared there and assigned later, so it is
/// GSH0003 at its designation; a ref local is a pointer and a span over an array is a view, and both are declared
/// there.
/// </summary>
[Category(TestCategory.Compiler)]
public class SwitchSectionLocalDiagnosticsTests
{
    private const string Cases = "Diagnostics/SwitchSectionLocals.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SharedRefStructOutVar_CrossSectionScope_ReportsGsh0003AtDesignation(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Cases, dialect);

        // The ref local of SharedRefLocalKernel is a pointer, declared before the switch: it is not reported

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 69, 33, 69, 39)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "used by another switch section"));
    }
}
