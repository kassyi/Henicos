using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>nameof</c> in GPU code: where its value stays a string on the device, the string is reported (GSH0002) at the
/// <c>nameof</c> expression; its length and a comparison of constant strings are values and are not reported. The
/// generated source never names <c>nameof</c>. The length of a null constant string throws in C# for certain, which is
/// reported (GSH0003) instead of written as the length of a null pointer.
/// </summary>
[Category(TestCategory.Compiler)]
public class NameofDiagnosticsTests
{
    private const string TestCase = "Diagnostics/NameofString.cu.cs";

    // nameof(NameofStringKernels) on line 13 becomes a span, so the string itself reaches the device; line 14 reads an
    // element and line 15 a length and a comparison, which are values; the only GSH0003 is the null constant's Length
    // on line 25
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NameofString_ConvertedToASpan_ReportsGsh0002AtTheNameof(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 13, 32, 13, 59)],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
        Assert.Equal("The string type is not supported in GPU kernels",
            Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.StringNotSupported.Id).Message);
        Assert.Equal([(Analyzer, 25, 16, 25, 30)], diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    // Missing.Length on line 25, where Missing is a null constant string
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NullConstantStringLength_KnownToThrow_ReportsGsh0003AtTheAccess(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        var reported = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
        Assert.Equal(
            (25, 16, 25, 30),
            (reported.StartLine, reported.StartColumn, reported.EndLine, reported.EndColumn));
        Assert.Equal(
            "The syntax 'Length of a null constant string (it throws NullReferenceException)' is not supported in " +
            "GSharp GPU kernels",
            reported.Message);
    }

    // Without the analyzers, as in the projects under tests/ (#463), the generator states the failure with its place
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NameofString_GeneratedSource_StatesGsh0002AndNeverNamesNameof(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [("(13,32): GSH0002", true), ("(25,16): GSH0003", true)],
            KernelDiagnostics.FindStatedFailures(run)
                .Select(static f => (f.Failure[f.Failure.LastIndexOf('(')..], f.IsReported))
                .Order());
        // The C++ of the length and of the name converted to a span, which no dialect defines; the launcher's own
        // nameof(r) in C# is not device code
        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        var generated = string.Concat(run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)));
        Assert.DoesNotContain("nameof(r).length", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(NameofStringKernels)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("nullptr.length", generated, StringComparison.Ordinal);
    }
}
