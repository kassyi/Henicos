using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>nameof</c> in GPU code: where its value stays a string on the device, the string is reported (GSH0002) at the
/// <c>nameof</c> expression; its length and a comparison of constant strings are values and are not reported. The
/// generated source never names <c>nameof</c>.
/// </summary>
[Category(TestCategory.Compiler)]
public class NameofDiagnosticsTests
{
    private const string TestCase = "Diagnostics/NameofString.cu.cs";

    // nameof(NameofStringKernels)[0] on line 12 indexes the string; line 14 uses a length and a comparison
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NameofString_IndexedOnTheDevice_ReportsGsh0002AtTheNameof(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 12, 17, 12, 44)],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
        Assert.Equal("The string type is not supported in GPU kernels",
            Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.StringNotSupported.Id).Message);
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
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
            [("(12,17): GSH0002", true)],
            KernelDiagnostics.FindStatedFailures(run)
                .Select(static f => (f.Failure[f.Failure.LastIndexOf('(')..], f.IsReported)));
        // The C++ of the length and of the indexed name, which no dialect defines; the launcher's own nameof(r) in C#
        // is not device code
        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        var generated = string.Concat(run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)));
        Assert.DoesNotContain("nameof(r).length", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(NameofStringKernels)", generated, StringComparison.Ordinal);
    }
}
