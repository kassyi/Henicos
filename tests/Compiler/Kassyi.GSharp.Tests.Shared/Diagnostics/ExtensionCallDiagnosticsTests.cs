using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// An extension method call of a <c>[GpuDeviceFunction]</c> (<c>x.M(a)</c>) is the static call <c>M(x, a)</c> in the
/// native output. C++ leaves the order of its arguments unspecified, so a receiver that an argument interferes with is
/// reported (GSH0003) instead of reordered; so is the call of one that suspends at a barrier, whose frame the static
/// call cannot pass.
/// </summary>
[Category(TestCategory.Compiler)]
public class ExtensionCallDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ExtensionCallDiagnostics.cs";

    // Line 25: value.Plus(value++), the argument writes the receiver that C# reads first; line 31: AfterBarrier()
    // reaches a barrier. IndependentReceiverKernel (line 38) is not reported.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ExtensionCall_ReportsInterferingReceiverAndSuspendingCallee(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 25, 35, 25, 54)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id,
                InvocationExpressionTranspilerRule.ExtensionReceiverInterferes));
        Assert.Equal(
            [(Analyzer, 31, 35, 31, 69)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id,
                InvocationExpressionTranspilerRule.SuspendingExtensionCall));
        Assert.DoesNotContain(diagnostics, static d => d.Origin == DiagnosticOrigin.Generator);
    }
}
