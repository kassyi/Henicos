using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The assignment forms that the device cannot write are reported once, at the construct, as GSH0003 (never as a C++
/// build error): a deconstruction of a value that is not a tuple literal or whose value is used, a conditional
/// deconstruction of a generic element type (#1102), and a compound or <c>??=</c> assignment of a computed property
/// with a lifted operator (#908).
/// </summary>
[Category(TestCategory.Compiler)]
public class AssignmentFormsDiagnosticsTests
{
    private const string TestCase = "Diagnostics/AssignmentFormsUnsupported.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Deconstruction_NonTupleValue_ReportsGsh0003AtValue(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 37, 18, 37, 23)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "deconstruction of a value that is not a tuple"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Deconstruction_ValueIsUsed_ReportsGsh0003AtAssignment(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 46, 20, 46, 35)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "deconstruction assignment whose value is used"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ComputedProperty_LiftedOperator_ReportsGsh0003AtAssignment(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 54, 9, 54, 21)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "with a user-defined or lifted operator"));
        Assert.Equal(
            [(Analyzer, 55, 9, 55, 22)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "??= of a computed property"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalDeconstruction_GenericType_ReportsGsh0003AtConditional(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 62, 18, 62, 40)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "conditional whose element type is generic"));
    }
}
