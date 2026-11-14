using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A variable of type <c>object</c>, an <c>is</c> test of a reference operand and every <c>as</c> need a heap object or
/// run-time type information (GSH0003); an <c>is</c> test of a value is decided by its type and reports nothing.
/// </summary>
[Category(TestCategory.Compiler)]
public class TypeTestDiagnosticsTests
{
    private const string TestCase = "Diagnostics/TypeTestForms.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectVariable_TypeTestOfReference_ReportsGsh0003AtTypeAndTest(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 17, 9, 17, 15)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "variable of type object"));
        Assert.Equal(
            [(Analyzer, 18, 19, 18, 27)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "needs the run-time type of a heap object")
                .Where(static r => r.StartLine == 18)
                .ToList());
    }

    // Lines 25 to 27 test values, which C# decides by their types, and the last kernel arrays and nullables, which are
    // null tests; line 34 converts a value to an interface with as
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task AsConversion_Value_ReportsGsh0003AndTypeTestOfValueReportsNothing(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 34, 20, 34, 43)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "needs the run-time type of a heap object")
                .Where(static r => r.StartLine == 34)
                .ToList());
        Assert.Equal(4, diagnostics.Count(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // Line 54 declares a variable of an interface, a reference to a heap object; line 55 declares an array of the
    // operand's own type, which is fine
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeclarationPattern_VariableOfInterface_ReportsGsh0003AtThePattern(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 54, 19, 54, 48)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "needs the run-time type of a heap object")
                .Where(static r => r.StartLine == 54)
                .ToList());
    }
}
