using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0035 follows a parallel-dependent call whatever the syntax that makes it: a constructor reached through
/// <c>new T()</c> in a generic struct or a target-typed <c>new()</c> (#953), and a getter reached through a bare
/// property name or <c>using static</c>, an indexer, a user-defined unary operator and a user-defined conversion
/// (#1262). Correct code in the same single-thread branch stays silent.
/// </summary>
[Category(TestCategory.Compiler)]
public class ParallelDependentCallFormsDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ParallelDependentCallForms.cu.cs";

    // Kernel, then the range of its one GSH0035 (start line, start column, end line, end column)
    [Theory]
    [InlineData("Cpu", "ExplicitNewKernel", 96, 20, 96, 30)]
    [InlineData("Cpu", "GenericNewKernel", 106, 20, 106, 34)]
    [InlineData("Cpu", "TargetTypedNewKernel", 116, 22, 116, 27)]
    [InlineData("Cpu", "BarePropertyKernel", 128, 20, 128, 29)]
    [InlineData("Cpu", "UsingStaticKernel", 138, 20, 138, 26)]
    [InlineData("Cpu", "IndexerKernel", 149, 20, 149, 26)]
    [InlineData("Cpu", "UnaryOperatorKernel", 160, 27, 160, 31)]
    [InlineData("Cpu", "ConversionKernel", 172, 29, 172, 32)]
    [InlineData("Cuda", "ExplicitNewKernel", 96, 20, 96, 30)]
    [InlineData("Cuda", "GenericNewKernel", 106, 20, 106, 34)]
    [InlineData("Cuda", "TargetTypedNewKernel", 116, 22, 116, 27)]
    [InlineData("Cuda", "BarePropertyKernel", 128, 20, 128, 29)]
    [InlineData("Cuda", "UsingStaticKernel", 138, 20, 138, 26)]
    [InlineData("Cuda", "IndexerKernel", 149, 20, 149, 26)]
    [InlineData("Cuda", "UnaryOperatorKernel", 160, 27, 160, 31)]
    [InlineData("Cuda", "ConversionKernel", 172, 29, 172, 32)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CallForm_ReportsGsh0035AtTheCall(string dialect, string kernel, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var ranges = await GetRangesInAsync(dialect, kernel);

        Assert.Equal([(Analyzer, startLine, startColumn, endLine, endColumn)], ranges);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task ParallelDependentCallForms_ValidSyntax_ReportsNothing(string dialect)
    {
        var ranges = await GetRangesInAsync(dialect, "CorrectCodeKernel");

        Assert.Empty(ranges);
    }

    // The GSH0035 ranges inside the declaration of kernel
    private static async Task<List<(DiagnosticOrigin, int, int, int, int)>> GetRangesInAsync(string dialect,
        string kernel)
    {
        var source = RoslynTestUtility.ReadTestCase(TestCase);
        var declaration = RoslynTestUtility.CreateCompilation(source).SyntaxTrees.Last().GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == kernel)
            .GetLocation()
            .GetLineSpan()
            .Span;
        var first = declaration.Start.Line + 1;
        var last = declaration.End.Line + 1;

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        return diagnostics.Ranges(GSharpDiagnostics.ParallelDependentCallFromSingleThreadContext.Id)
            .Where(range => range.StartLine >= first && range.StartLine <= last)
            .ToList();
    }
}
