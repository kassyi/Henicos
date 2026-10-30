using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>case</c> is written with a C++ label named after its value (<c>__gsharp_switch_1_state_m1</c>), so a value that
/// is not an integer, <c>char</c>, <c>bool</c> or enum constant is reported (GSH0003) with the reason.
/// </summary>
[Category(TestCategory.Compiler)]
public class SwitchCaseValueDiagnosticsTests
{
    private const string TestCase = "Diagnostics/SwitchCaseValueDiagnostics.cs";

    // Line 15: case 1.5f
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FloatCaseValue_ReportsUnsupportedCaseValue(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 15, 22, 15, 26)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, SwitchStateLabel.UnsupportedCaseValue));
    }
}
