namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A span local that the CPU state machine declares again on every resume cannot carry a value from one state into
/// another. The generator finds it on the lowered state machine and reports GSH0039 at the declaration, also for the
/// splits that the analyzer's flow graph does not have: a loop around a barrier, a span written through <c>out</c> by a
/// call that suspends, and the reconvergence point that <c>Warp.ActiveMask()</c> adds.
/// </summary>
[Category(TestCategory.Compiler)]
public class SpanAcrossStatesDiagnosticsTests
{
    private const string TestCase = "Diagnostics/SpanAcrossStates.cu.cs";

    private static string Gsh0039 => GSharpDiagnostics.CrossBarrierRefStructProhibited.Id;

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SpanLocal_CarriedIntoAnotherState_ReportsGsh0039FromTheGeneratorAtTheDeclaration()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        // mutant: SpanAcrossStates.Find without RemoveReportedByAnalyzer -> the first span is reported twice
        // mutant: SpanAcrossStates.EventWalker.VisitArgument counting the out argument of a suspending call as a write
        // -> the second one is lost
        // mutant: SpanAcrossStates.EventWalker.VisitArgument counting no out argument as a write -> the span that
        // WrittenByOutInLoop writes in every iteration is reported
        // mutant: SpanAcrossStates.EventWalker.VisitBlock without the early return -> UsedInOtherBranch is reported
        // Declared before a loop that has a barrier (the analyzer reports it, so the generator does not repeat it),
        // written through out by a suspending call, and live only across a reconvergence point; the other spans are
        // declared in a loop body, created again after a barrier, written through out by a call that does not
        // suspend, or used only on the branch without the barrier, and are not reported
        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 18, 19, 18, 22),
                (DiagnosticOrigin.Generator, 40, 35, 40, 36),
                (DiagnosticOrigin.Generator, 49, 19, 49, 20)
            ],
            diagnostics.Ranges(Gsh0039));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task SpanLocal_CarriedIntoAnotherState_IsNotReportedByTheGeneratorOnTheCudaDialect()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cuda");

        // The CUDA dialect keeps every thread on the GPU and splits nothing (#929): only the analyzer reports
        Assert.Equal([(DiagnosticOrigin.Analyzer, 18, 19, 18, 22)], diagnostics.Ranges(Gsh0039));
    }
}
