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

    // Loop macro bodies with and without a context, and an unroll hint inside a switch section
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_SupportedMacroForms_ReportNothing(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/SupportedMacroAndArrayForms.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.MacroLambdaRequiresParameter.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.MacroArgumentMustBeConstant.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnrollHintMustPrecedeLoop.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant.Id));
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

    // A return in the body ends the kernel in the generated code, not the iteration. Only the sad kernels report: the
    // lists hold every report of the case, so the kernels that keep to the rules are checked as well
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ReturnInLoopMacroBody_ReportsGsh0003AtTheReturn(string dialect)
    {
        var diagnostics =
            await KernelDiagnostics.RunTestCaseAsync("Diagnostics/MacroLoopBodyRestrictions.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 32, 25, 32, 32), (Analyzer, 55, 25, 55, 32)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "return in a Gpu.ForUnroll body"));
        Assert.Equal(
            [(Analyzer, 43, 25, 43, 32), (Analyzer, 68, 25, 68, 32)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "return in a Gpu.GridStrideLoop body"));
    }

    // The loop variable is replaced by the iteration index in the generated code, which cannot be assigned
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_AssignmentToLoopVariable_ReportsGsh0003AtTheWrite(string dialect)
    {
        var diagnostics =
            await KernelDiagnostics.RunTestCaseAsync("Diagnostics/MacroLoopBodyRestrictions.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 80, 13, 80, 18),
                (Analyzer, 81, 13, 81, 19),
                (Analyzer, 82, 13, 82, 16),
                (Analyzer, 83, 13, 83, 16),
                (Analyzer, 84, 18, 84, 23),
                (Analyzer, 85, 19, 85, 24),
                (Analyzer, 86, 13, 86, 34)
            ],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "assignment to the Gpu.ForUnroll loop variable 'i'"));
        Assert.Equal(
            [(Analyzer, 97, 13, 97, 19)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "assignment to the Gpu.GridStrideLoop loop variable 'i'"));
    }

    // An in argument or an address would be the address of the iteration index
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ReferenceToLoopVariable_ReportsGsh0003AtTheReference(string dialect)
    {
        var diagnostics =
            await KernelDiagnostics.RunTestCaseAsync("Diagnostics/MacroLoopBodyRestrictions.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 108, 31, 108, 35), (Analyzer, 109, 28, 109, 30)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "reference to the Gpu.ForUnroll loop variable 'i'"));
        Assert.Equal(
            [(Analyzer, 119, 31, 119, 35)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "reference to the Gpu.GridStrideLoop loop variable 'i'"));
    }
}
