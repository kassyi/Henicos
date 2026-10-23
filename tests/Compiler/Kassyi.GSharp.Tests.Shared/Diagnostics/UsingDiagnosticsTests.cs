using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzers reject a <c>using</c> that device code cannot dispose: one whose scope holds a barrier, where the CPU
/// state machine would leave the scope and dispose the resource, and one whose resource is not a user struct.
/// </summary>
[Category(TestCategory.Compiler)]
public class UsingDiagnosticsTests
{
    private const string TestCase = "Diagnostics/UsingBarrierScope.cu.cs";

    // Line 30: in a using statement; line 34: after a using declaration of the body; line 41: in a nested block with a
    // using declaration after the barrier. BarrierOutsideUsingKernel (lines 48 to 58) is not reported.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_BarrierInTheScopeOfAUsing_ReportsGsh0003AtTheBarrier(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 30, 13, 30, 30),
                (Analyzer, 34, 9, 34, 26),
                (Analyzer, 41, 13, 41, 30)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "barrier in the scope of a using"));
    }

    // Line 64: the class cannot be created (GSH0001), and the using has no struct to dispose (GSH0003, at 'var')
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UsingOfAClass_ReportsTheCreationAndTheResource(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal([(Analyzer, 64, 31, 64, 55)], diagnostics.Ranges(GSharpDiagnostics.ObjectCreationNotSupported.Id));
        Assert.Equal(
            [(Analyzer, 64, 16, 64, 19)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "not a user struct"));
    }
}
