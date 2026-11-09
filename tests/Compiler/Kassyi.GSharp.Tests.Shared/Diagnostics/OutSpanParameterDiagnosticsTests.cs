namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0039 takes a span parameter overwritten through an <c>out</c> argument as written, as it does for a local: a
/// change before a barrier or shuffle followed by the overwrite is not live across the split; a read before the
/// overwrite still is (#1003).
/// </summary>
[Category(TestCategory.Compiler)]
public class OutSpanParameterDiagnosticsTests
{
    private const string TestCase = "Diagnostics/OutSpanParameterOverwrite.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_SpanParameterOverwrittenThroughOut_IsNotLiveAcrossTheSplit(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // GSH0039 is reported at the declaration of the method that holds the parameter: only ReadBeforeOverwrite (44)
        Assert.Equal(
            [44],
            diagnostics.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id)
                .Select(static range => range.StartLine)
                .Distinct()
                .ToList());
    }
}
