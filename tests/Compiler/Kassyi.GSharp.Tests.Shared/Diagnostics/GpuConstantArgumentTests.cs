using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>Gpu.Constant</c> writes the value of its argument, the name of the macro, when the argument is a constant string
/// (a literal, <c>nameof</c>, a <c>const</c> field of the class or of another one, a concatenation of constants); an
/// argument that is not a constant reports GSH0012 at the argument and is never written as an empty expression.
/// </summary>
[Category(TestCategory.Compiler)]
public class GpuConstantArgumentTests
{
    private const string TestCase = "Diagnostics/GpuConstantArguments.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task GpuConstant_ConstantStringArguments_WritesTheMacroNames(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        var generated = string.Concat(run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)));
        // The element writes go through the null check of the kernel's array parameter: __gsharp_array_at(<array>, <i>, <site>u)
        Assert.Matches(@", 0, \d+u\)=GpuConstantMacroNames;", generated);
        Assert.Matches(@", 1, \d+u\)=FOO_MACRO;", generated);
        Assert.Matches(@", 2, \d+u\)=WIDTH_MACRO;", generated);
        Assert.Matches(@", 3, \d+u\)=AB;", generated);
        Assert.Matches(@", 4, \d+u\)=LITERAL_MACRO;", generated);
        Assert.DoesNotContain("u)=;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("u)=Macro;", generated, StringComparison.Ordinal);
    }

    // Gpu.Constant(which == 0 ? "A" : "B") on line 30
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task GpuConstant_ArgumentNotConstant_ReportsGsh0012AtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 30, 29, 30, 51)],
            diagnostics.Ranges(GSharpDiagnostics.MacroArgumentMustBeConstant.Id));
        Assert.Equal(
            "The argument 'macroName' of macro 'Constant' must be a compile-time constant",
            Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.MacroArgumentMustBeConstant.Id).Message);
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }
}
