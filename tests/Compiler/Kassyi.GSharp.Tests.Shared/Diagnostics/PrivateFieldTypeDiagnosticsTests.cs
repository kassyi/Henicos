namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A kernel parameter struct with a field whose type the generated launcher cannot name (a private type nested in the
/// struct): G# reports GSH0108 at the parameter type instead of leaving CS0122 in generated code.
/// </summary>
[Category(TestCategory.Compiler)]
public class PrivateFieldTypeDiagnosticsTests
{
    private const string TestCase = "Diagnostics/PrivateFieldTypeInaccessible.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task PrivateNestedFieldType_KernelParameter_ReportsGsh0108AtTheParameterType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // A converted struct, a struct copied as one block, and a private nested type that is converted itself
        // mutant: StructLayoutRule.TryFindInaccessibleFieldType `!compilation.IsSymbolAccessibleWithin(...)` ->
        // `false` -> nothing is reported
        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 51, 41, 51, 57),
                (DiagnosticOrigin.Analyzer, 58, 50, 58, 69),
                (DiagnosticOrigin.Analyzer, 65, 50, 65, 75),
            ],
            diagnostics.Ranges(GSharpDiagnostics.StructFieldTypeInaccessibleToLauncher.Id));
        const string FieldPart = "HiddenTypeHolder.Hidden' of field '_hidden' is not accessible from";
        const string Ending =
            ".PrivateFieldTypeInaccessibleKernels', where the generated launcher copies the field. " +
            "Make the type internal or public.";
        Assert.Contains(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.StructFieldTypeInaccessibleToLauncher.Id &&
                        d.Message.Contains(FieldPart, StringComparison.Ordinal) &&
                        d.Message.EndsWith(Ending, StringComparison.Ordinal));
    }
}
