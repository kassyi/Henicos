namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A kernel parameter struct with a field whose type the generated launcher cannot name (a private type nested in the
/// struct): G# reports GSH0108 at the parameter type, and the generator writes a launcher that throws instead of code
/// that would fail with CS0122, so that a build shows GSH0108.
/// </summary>
[Category(TestCategory.Compiler)]
public class PrivateFieldTypeDiagnosticsTests
{
    private const string TestCase = "Diagnostics/PrivateFieldTypeInaccessible.cu.cs";

    private static string Gsh0108 => GSharpDiagnostics.StructFieldTypeInaccessibleToLauncher.Id;

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task PrivateNestedFieldType_KernelParameter_ReportsGsh0108AtTheParameterType(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // A converted struct, a struct copied as one block, a private nested type that is converted itself, the same
        // type one struct deeper, and a private element type of an array field
        // mutant: LauncherTypeAccess.FindInMember `!compilation.IsSymbolAccessibleWithin(...)` -> `false` -> nothing
        // is reported
        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 72, 41, 72, 57),
                (DiagnosticOrigin.Analyzer, 79, 50, 79, 69),
                (DiagnosticOrigin.Analyzer, 86, 50, 86, 75),
                (DiagnosticOrigin.Analyzer, 93, 46, 93, 61),
                (DiagnosticOrigin.Analyzer, 100, 44, 100, 63),
            ],
            diagnostics.Ranges(Gsh0108));
    }

    [Theory]
    [InlineData(72, "HiddenTypeHolder.Hidden' of field '_hidden'")]
    [InlineData(93, "HiddenTypeHolder.Hidden' of field 'Inner._hidden'")]
    [InlineData(100, "HiddenElementHolder.Item' of field '_items'")]
    [Scenario(TestScenario.SadPath)]
    public async Task PrivateNestedFieldType_FieldPath_NamesTheTypeAndThePathFromTheParameter(int line, string part)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        // mutant: LauncherTypeAccess.FindInMember `member.Name + "." + inner.FieldPath` -> `inner.FieldPath` -> the
        // nested case names '_hidden' only
        var reported = Assert.Single(diagnostics, d => d.Id == Gsh0108 && d.StartLine == line);
        Assert.Contains(part, reported.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            ".PrivateFieldTypeInaccessibleKernels', where the generated launcher copies the field. " +
            "Make the type internal or public.",
            reported.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task PrivateNestedFieldType_GeneratedLauncher_CompilesAndThrowsInstead(string dialect)
    {
        var path = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases", TestCase);
        var compilation = RoslynTestUtility.CreateCompilationAt(path, RoslynTestUtility.ReadTestCase(TestCase));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        // A build shows the analyzer's diagnostics only when the C# compiles: no error may come from generated code,
        // or the user would see CS0122 and no GSH0108
        // mutant: KernelHostModel.Create `var hasLauncher = unavailableReason is null;` -> `true` -> CS0122 in the
        // shadow structs and launchers
        var generatedErrors = run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.Empty(generatedErrors);

        var launchers = string.Join(
            "\n",
            run.Output.SyntaxTrees
                .Where(tree => !compilation.SyntaxTrees.Contains(tree))
                .Select(static tree => tree.ToString()));
        Assert.Contains(
            "throw new global::System.NotSupportedException(\"HiddenTypeOuterKernel cannot be launched: the type " +
            "'Kassyi.GSharp.Tests.TestCases.Diagnostics.HiddenTypeHolder.Hidden' of field 'Inner._hidden' is not " +
            "accessible from 'Kassyi.GSharp.Tests.TestCases.Diagnostics.PrivateFieldTypeInaccessibleKernels' " +
            "(GSH0108).\")",
            launchers,
            StringComparison.Ordinal);
    }
}
