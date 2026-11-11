namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The form of a property whose accessors use the <c>field</c> keyword that G# reports instead of emitting: an
/// operator macro whose target is such a property, which would write through a reference and skip the setter.
/// </summary>
[Category(TestCategory.Compiler)]
public class FieldKeywordPropertyDiagnosticsTests
{
    private const string TestCase = "Diagnostics/FieldKeywordPropertyUnsupported.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task FieldKeywordProperty_OperatorMacroTarget_ReportsGsh0003AtTheTarget(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 42, 9, 42, 25)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "is a property without storage"));
    }
}
