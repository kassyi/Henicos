using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports the misuse of a <c>Gpu</c> macro once, at the argument or call; the generator reports none of
/// them (S-46, #461).
/// </summary>
[Category(TestCategory.Compiler)]
public class MacroDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ForUnrollVariableCount_ReportsOnlyGsh0012AtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/MacroArgumentSadPath.cs", dialect);

        Assert.Equal([(Analyzer, 14, 26, 14, 31)], diagnostics.Ranges(GSharpDiagnostics.MacroArgumentMustBeConstant.Id));
        // The transpiler used to report the whole call as GSH0003
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "ForUnroll"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SyncGridWithoutLambda_ReportsGsh0003AtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/MacroArgumentSadPath.cs", dialect);

        Assert.Equal(
            [(Analyzer, 25, 22, 25, 27)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "Gpu.SyncGrid"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnrollHint_ReportsGsh0016AndGsh0017Once(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/UnrollHintSadPath.cs", dialect);

        Assert.Equal([(Analyzer, 11, 9, 11, 26)], diagnostics.Ranges(GSharpDiagnostics.UnrollHintMustPrecedeLoop.Id));
        Assert.Equal(
            [(Analyzer, 15, 25, 15, 27)],
            diagnostics.Ranges(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant.Id));
    }

    // A loop macro body without a loop variable does not bind to the macro (CS1593), so nothing reports GSH0011
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_LoopMacroLambdaWithoutParameter_IsNotAMacroCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/MacroLambdaWithoutParameter.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.MacroLambdaRequiresParameter.Id));
    }
}
