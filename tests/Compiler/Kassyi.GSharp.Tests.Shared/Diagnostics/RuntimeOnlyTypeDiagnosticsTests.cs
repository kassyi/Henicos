using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports each reference to <c>dynamic</c>, <see cref="TypedReference"/> and <c>GCHandle</c> in device code
/// as GSH0003, decided by the symbol the name binds to; the generator reports none of them (S-46).
/// </summary>
[Category(TestCategory.Compiler)]
public class RuntimeOnlyTypeDiagnosticsTests
{
    private const string TestCase = "Diagnostics/RuntimeOnlyTypes.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_Dynamic_ReportsGsh0003AtTheType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal([(Analyzer, 14, 13, 14, 20)], diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'dynamic'"));
    }

    // The type on line 23; __makeref is reported by the transpiler as a syntax kind of its own
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_TypedReference_ReportsGsh0003AtTheTypeAndMakeRef(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 23, 13, 23, 27)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'TypedReference'"));
        Assert.Equal(
            [(Analyzer, 23, 40, 23, 56)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'MakeRefExpression'"));
    }

    // The type and the receiver of Alloc on line 31, and the var on line 32; the user struct named GCHandle (line 40) is
    // not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_GCHandle_ReportsEachReferenceAndNotAUserTypeOfTheSameName(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 31, 13, 31, 21),
                (Analyzer, 31, 31, 31, 39),
                (Analyzer, 32, 13, 32, 16)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'GCHandle'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RuntimeOnlyTypes_AreNotReportedByTheGenerator(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(diagnostics, static d => d.Origin == DiagnosticOrigin.Generator);
    }
}
