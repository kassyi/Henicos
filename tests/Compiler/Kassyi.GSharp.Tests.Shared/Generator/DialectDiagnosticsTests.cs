using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>The generator reports the diagnostics of the dialect it emits, once.</summary>
[Category(TestCategory.Compiler)]
public class DialectDiagnosticsTests
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Generator/DialectDiagnostics.cu.cs");

    // The C++ dialect's own failures used to be dropped, so a CPU build said nothing about the replaced call
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CpuDialect_ReportsInlinePtxOncePerCall()
    {
        var (_, _, diagnostics) = RoslynTestUtility.RunGenerator(s_source, "Cpu");

        var inlinePtx = diagnostics.Where(static d => d.Id == GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id).ToList();
        Assert.Equal(2, inlinePtx.Count);

        // The kernel is transpiled from its lowered body; its diagnostic still points at the call in the source
        var lines = inlinePtx.Select(static d => d.Location.GetLineSpan().StartLinePosition.Line + 1).Order().ToList();
        Assert.All(inlinePtx, static d => Assert.True(d.Location.IsInSource));
        Assert.Equal([11, 18], lines);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CudaDialect_DoesNotReportTheCppDialectsFailures()
    {
        var (_, _, diagnostics) = RoslynTestUtility.RunGenerator(s_source, "Cuda");

        Assert.DoesNotContain(diagnostics, static d => d.Id == GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id);
    }
}
