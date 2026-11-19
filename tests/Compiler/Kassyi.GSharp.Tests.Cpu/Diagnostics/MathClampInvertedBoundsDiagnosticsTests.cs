using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0122: a <c>Math.Clamp</c> whose <c>min</c> and <c>max</c> are constants with <c>min &gt; max</c> is an error at
/// the call, on every dialect, as .NET throws <c>ArgumentException</c> whenever it runs. Equal bounds, NaN and
/// <c>-0.0</c> do not throw, and a bound known only at run time is checked when the call runs (#718).
/// </summary>
[Category(TestCategory.Compiler)]
public class MathClampInvertedBoundsDiagnosticsTests
{
    private const string SadPath = "Diagnostics/MathClampInvertedBounds.cu.cs";

    // Lines 18 to 23: literals, const field and local, long, byte, double, named arguments. Line 28: a device function.
    // AcceptedBoundsKernel (lines 35 to 40) is not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task InvertedConstantBounds_EveryForm_ReportsGsh0122AtTheCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(SadPath, dialect);

        Assert.Equal(
            [
                (Analyzer, 18, 16, 18, 39),
                (Analyzer, 19, 16, 19, 52),
                (Analyzer, 20, 16, 20, 41),
                (Analyzer, 21, 16, 21, 51),
                (Analyzer, 22, 16, 22, 43),
                (Analyzer, 23, 16, 23, 56),
                (Analyzer, 28, 57, 28, 80)
            ],
            diagnostics.Ranges(GSharpDiagnostics.MathClampMinGreaterThanMax.Id));
        var messages = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.MathClampMinGreaterThanMax.Id)
            .Select(static d => d.Message)
            .ToList();
        Assert.Equal(
            "Math.Clamp is called with min 10 greater than max 5, so the call always throws ArgumentException. " +
            "If the bounds are swapped, write Math.Clamp(value, 5, 10).",
            messages[0]);
        Assert.Contains("min 1 greater than max 0.5", messages[4]);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task InvertedConstantBounds_DiagnosticSeverity_ReportsError(string dialect)
    {
        var (_, analyzer, _) =
            await KernelDiagnostics.GetDiagnosticsAsync(RoslynTestUtility.ReadTestCase(SadPath), dialect);

        var reported = analyzer
            .Where(static d => d.Id == GSharpDiagnostics.MathClampMinGreaterThanMax.Id)
            .ToList();

        Assert.Equal(7, reported.Count);
        Assert.All(reported, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }
}
