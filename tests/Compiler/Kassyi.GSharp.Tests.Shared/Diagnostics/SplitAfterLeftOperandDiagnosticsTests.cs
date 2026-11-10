using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0039 follows the lowering, which evaluates the left operand of a binary expression before a split in its right
/// operand, as C# does (#1535): a span changed in the left operand and read after the split is live across it; one
/// only read in the left operand is not.
/// </summary>
[Category(TestCategory.Compiler)]
public class SplitAfterLeftOperandDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SpanChangedInTheLeftOperand_ReportsGsh0039AtTheParameter(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/SplitAfterLeftOperandChange.cu.cs",
            dialect);

        Assert.Equal(
            [(Analyzer, 25, 54, 25, 55)],
            diagnostics.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id));
    }
}
