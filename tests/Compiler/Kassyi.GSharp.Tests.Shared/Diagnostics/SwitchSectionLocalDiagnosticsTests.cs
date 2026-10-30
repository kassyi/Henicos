namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A local that one switch section declares and another uses is declared before the first case label in the C++ (#795).
/// A span or a ref local cannot be declared there and assigned later, so it is GSH0003 at its declarator.
/// </summary>
[Category(TestCategory.Compiler)]
public class SwitchSectionLocalDiagnosticsTests
{
    private const string Cases = "Diagnostics/SwitchSectionLocals.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SpanOrRefLocalSharedBySections_ReportsGsh0003AtTheDeclarator(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(Cases, dialect);

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 17, 27, 17, 35),
                (DiagnosticOrigin.Analyzer, 34, 25, 34, 40)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "used by another switch section"));
    }
}
