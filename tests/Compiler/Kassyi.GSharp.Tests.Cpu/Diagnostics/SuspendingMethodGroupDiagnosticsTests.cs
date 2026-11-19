using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>[GpuDeviceFunction]</c> that reaches a barrier or warp shuffle cannot be referenced without calling it
/// (method group / delegate / StackLinq selector), because the CPU state machine cannot split a call through a
/// delegate (GSH0062). A macro body method group still reports GSH0003 alone, and non-suspending or valid calls
/// report nothing.
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingMethodGroupDiagnosticsTests
{
    private const string TestCase = "Diagnostics/SuspendingMethodGroup.cs";

    private const string ExpectedMessageFragment =
        "is a device function that reaches Gpu.SyncThreads() or a warp shuffle, " +
        "so it cannot be called through a delegate";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingMethodGroup_DelegateAndLinqForms_ReportsSingleGsh0062AtReference(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 57, 29, 57, 33),
                (Analyzer, 64, 19, 64, 23),
                (Analyzer, 70, 68, 70, 77),
                (Analyzer, 76, 29, 76, 40),
                (Analyzer, 83, 24, 83, 41)
            ],
            diagnostics.Ranges(GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id, ExpectedMessageFragment));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task MacroBodyMethodGroup_SuspendingFunction_ReportsOnlyGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 90, 33, 90, 37)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "a Gpu macro body must be a lambda"));
        Assert.Equal(
            [(Analyzer, 91, 26, 91, 34)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "Gpu.SyncGrid requires a lambda"));
        Assert.DoesNotContain(
            diagnostics.Ranges(GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id),
            static d => d.StartLine is 90 or 91);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task ValidSuspendingAndNonSuspendingForms_DirectNameofMacroLambda_ReportsNoDiagnostics(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(diagnostics, static d => d.StartLine >= 94);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingMethodGroup_GeneratedSource_StatesEachFailureWhereAnalyzerReportsIt(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var stated = KernelDiagnostics.FindStatedFailures(run);
        Assert.NotEmpty(stated);
        Assert.All(stated, static f => Assert.True(f.IsReported, f.Failure));
        Assert.Equal(
            [
                ("(57,29): GSH0062", true),
                ("(64,19): GSH0062", true),
                ("(70,68): GSH0062", true),
                ("(76,29): GSH0062", true),
                ("(83,24): GSH0062", true)
            ],
            stated.Where(static f => f.Failure.EndsWith("GSH0062", StringComparison.Ordinal)).ToArray());
    }
}
