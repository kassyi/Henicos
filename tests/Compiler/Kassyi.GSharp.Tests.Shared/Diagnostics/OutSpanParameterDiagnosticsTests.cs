using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0039 takes a span parameter overwritten through an <c>out</c> argument as written, as it does for a local: a
/// change before a barrier or shuffle followed by the overwrite is not live across the split; a read before the
/// overwrite still is (#1003).
/// </summary>
[Category(TestCategory.Compiler)]
public class OutSpanParameterDiagnosticsTests
{
    // The kernels that OutSpanParameterTests runs: the same source, checked here for diagnostics
    private static readonly string[] s_acceptedKernels =
        ["tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Syntax", "OutSpanParameterKernels.cu.cs"];

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_SpanParameterOverwrittenThroughOut_IsNotLiveAcrossTheSplit(string dialect)
    {
        var source = await File.ReadAllTextAsync(RepositoryPaths.Combine(s_acceptedKernels));

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id));
    }

    // GSH0039 is reported at the parameter of the method that holds it
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SpanParameterReadBeforeTheOverwrite_ReportsGsh0039(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OutSpanParameterOverwrite.cu.cs",
            dialect);

        Assert.Equal(
            [(Analyzer, 18, 67, 18, 68)],
            diagnostics.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id));
    }
}
