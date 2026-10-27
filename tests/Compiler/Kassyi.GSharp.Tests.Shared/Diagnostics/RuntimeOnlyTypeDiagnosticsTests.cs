using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports each reference to <c>dynamic</c>, <see cref="TypedReference"/>, <c>GCHandle</c> and
/// <c>decimal</c> in device code as GSH0003, decided by the symbol the name binds to; the generator reports none of them
/// (S-46).
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

    // decimal has no C++ type or literal: the keyword (lines 51, 57, 69), the literals (51, 53), the var (52), each reference
    // to the const field (50), the local (52, 53), the parameter (57) and the property (63), and a call that returns one
    // (63)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_Decimal_ReportsEachUseInDeviceCode(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 50, 33, 50, 37),
                (Analyzer, 51, 13, 51, 20),
                (Analyzer, 51, 29, 51, 33),
                (Analyzer, 52, 13, 52, 16),
                (Analyzer, 52, 24, 52, 29),
                (Analyzer, 53, 33, 53, 37),
                (Analyzer, 53, 46, 53, 50),
                (Analyzer, 57, 36, 57, 43),
                (Analyzer, 57, 62, 57, 67),
                (Analyzer, 63, 33, 63, 45),
                (Analyzer, 63, 56, 63, 76),
                (Analyzer, 69, 23, 69, 30),
                (Analyzer, 69, 59, 69, 66)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'decimal'").Order());
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
