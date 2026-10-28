using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A constant width of a warp shuffle that is not 1, 2, 4, 8, 16 or 32 is undefined on CUDA; the analyzer reports it as
/// GSH0095 at the width argument (#666). A width known only at run time is checked by the CPU paths when they run.
/// </summary>
[Category(TestCategory.Compiler)]
public class WarpShuffleWidthDiagnosticsTests
{
    // 3, 64, 0, -8 and a constant local of 12; not 16, a parameter, the default or 1
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ConstantWidthNotAPowerOfTwoUpTo32_ReportsGsh0095AtTheWidth(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/WarpShuffleWidthSadPath.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 14, 54, 14, 55),
                (Analyzer, 15, 49, 15, 51),
                (Analyzer, 16, 52, 16, 53),
                (Analyzer, 17, 52, 17, 54),
                (Analyzer, 18, 56, 18, 62)
            ],
            diagnostics.Ranges(GSharpDiagnostics.WarpShuffleWidthNotPowerOfTwo.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ConstantWidth_MessageNamesTheShuffleAndTheWidth(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/WarpShuffleWidthSadPath.cs", dialect);

        Assert.Single(diagnostics.Ranges(GSharpDiagnostics.WarpShuffleWidthNotPowerOfTwo.Id, "The width of Warp.ShuffleDownSync is 3"));
        Assert.Single(diagnostics.Ranges(GSharpDiagnostics.WarpShuffleWidthNotPowerOfTwo.Id, "The width of Warp.ShuffleSync is 12"));
    }
}
