using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The macro rules write a <c>Gpu</c> macro that takes a body, and <c>Gpu.Hint.Unroll</c>, only as a statement of its
/// own and with the body written as a lambda; anywhere else the call is GSH0003 instead of a name that no dialect
/// defines.
/// </summary>
[Category(TestCategory.Compiler)]
public class GpuMacroCallFormDiagnosticsTests
{
    private const string TestCase = "Diagnostics/GpuMacroCallForms.cs";

    // Gpu.SingleThread, Gpu.ForUnroll, Gpu.GridStrideLoop, Gpu.Hint.Unroll (lines 13 to 16) and Gpu.SyncGrid (line 27)
    // as the body of an expression-bodied local function: the whole call
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task MacroOutsideStatement_ExpressionBodiedLocalFunction_ReportsGsh0003AtCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 13, 40, 13, 72),
                (Analyzer, 14, 37, 14, 71),
                (Analyzer, 15, 38, 15, 74),
                (Analyzer, 16, 34, 16, 51),
                (Analyzer, 27, 32, 27, 60)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "outside a statement"));
    }

    // Gpu.SingleThread(Body), Gpu.ForUnroll(0, 2, Each) and Gpu.GridStrideLoop(4, Each): the body argument, as for
    // Gpu.SyncGrid; not a call that the dialect does not implement
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task MacroBody_MethodGroup_ReportsGsh0003AtTheBody(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 36, 30, 36, 34),
                (Analyzer, 37, 33, 37, 37),
                (Analyzer, 38, 35, 38, 39)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "a Gpu macro body must be a lambda"));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode.Id));
    }

    // The statements with lambda bodies (from line 54) are written by the macro rules
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task MacroStatement_LambdaBody_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(diagnostics, static d => d.StartLine >= 54);
    }

    // Without the analyzers, the generator states the same failures with their places
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task MacroCallForms_GeneratedSource_StatesEachFailureWhereTheAnalyzerReportsIt(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.NotEmpty(stated);
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
        Assert.DoesNotContain(stated, static f => f.Failure.EndsWith("GSH0109", StringComparison.Ordinal));
    }
}
