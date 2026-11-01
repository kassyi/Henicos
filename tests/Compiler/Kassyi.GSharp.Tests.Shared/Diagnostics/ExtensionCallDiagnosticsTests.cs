using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// An extension method call of a <c>[GpuDeviceFunction]</c> (<c>x.M(a)</c>) whose receiver and arguments interfere is
/// sequenced in C# order by <c>EvaluationOrder.BeginExtensionCall</c> (#640), so <c>InterferingReceiverKernel</c> is
/// not reported. A reduced extension call of a device function that suspends at a barrier is still reported (GSH0003),
/// as is a <c>StackLinq</c> call whose named arguments are reordered with interfering side effects.
/// </summary>
[Category(TestCategory.Compiler)]
public class ExtensionCallDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ExtensionCallDiagnostics.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ExtensionCall_ReportsSuspendingCalleeAndReorderedStackLinqArgs(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 25, 35, 25, 69)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id,
                InvocationExpressionTranspilerRule.SuspendingExtensionCall));
        Assert.Equal(
            [(Analyzer, 31, 35, 33, 86)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id,
                StackLinqTranspilerRule.ReorderedArguments));
        Assert.DoesNotContain(diagnostics, static d => d.Origin == DiagnosticOrigin.Generator);
    }
}
