using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A stackalloc whose size is not a constant is GSH0003 (dynamic array size) at its size in both dialects, also when a
/// barrier follows it and the CPU dialects declare the local again on every resume (ResumeLocals).
/// </summary>
[Category(TestCategory.Compiler)]
public class ResumeStackAllocSizeDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DynamicStackAllocSize_ReportsGsh0003AtTheSize(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/ResumeStackAllocSizeKernels.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 14, 38, 14, 39),
                (Analyzer, 24, 33, 24, 34),
                (Analyzer, 34, 38, 34, 39),
                (Analyzer, 44, 33, 44, 34),
                // (int*)stackalloc byte[16]: the stackalloc inside the cast
                (Analyzer, 52, 24, 52, 43)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }
}
