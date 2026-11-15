namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A local that one switch section declares and another uses is declared before the first case label in the C++ (#795).
/// A ref local and an out variable of a ref struct cannot be declared there and assigned later, so each is GSH0003 at its
/// declarator; a span over an array
/// is a view and is declared there.
/// </summary>
[Category(TestCategory.Compiler)]
public class SwitchSectionLocalDiagnosticsTests
{
    private const string Cases = "Diagnostics/SwitchSectionLocals.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SpanOrRefLocalSharedBySections_CrossSectionScope_ReportsGsh0003AtDeclarator(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Cases, dialect);

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 34, 25, 34, 40),
                (DiagnosticOrigin.Analyzer, 69, 33, 69, 39)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "used by another switch section"));
    }
}
