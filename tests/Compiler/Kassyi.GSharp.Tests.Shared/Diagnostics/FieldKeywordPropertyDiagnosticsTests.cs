using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The forms of a property whose accessors use the <c>field</c> keyword that G# reports instead of emitting: a
/// constructor that steps a property without a setter, and an operator macro whose target is such a property.
/// </summary>
[Category(TestCategory.Compiler)]
public class FieldKeywordPropertyDiagnosticsTests
{
    private const string TestCase = "Diagnostics/FieldKeywordPropertyUnsupported.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task FieldKeywordProperty_SteppedInConstructorWithoutSetter_ReportsGsh0003AtTheStep(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 12, 9, 12, 15)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, ComputedPropertyAssignment.StepOfBackingFieldWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task FieldKeywordProperty_OperatorMacroTarget_ReportsGsh0003AtTheTarget(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 63, 9, 63, 25)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "is a property without storage"));
    }
}
