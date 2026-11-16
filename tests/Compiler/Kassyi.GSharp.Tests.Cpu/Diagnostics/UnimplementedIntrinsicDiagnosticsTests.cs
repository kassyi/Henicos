using System.Text;
using Kassyi.GSharp.Compiler.Models;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A call of an intrinsic that the native API YAML declares the dialect does not implement (<c>unsupported</c>:
/// mbarrier, WMMA and the shared window address on the CPU dialect) is a GSH0054 warning at the call, from the
/// analyzer, and the generator writes a runtime failure in its place (#717). CUDA implements them and reports nothing.
/// </summary>
[Category(TestCategory.Compiler)]
public class UnimplementedIntrinsicDiagnosticsTests
{
    private const string TestCase = "Diagnostics/UnimplementedIntrinsics.cu.cs";

    // MBarrierArrive and MBarrierWait in a device function (lines 12 and 13), MBarrierInit, Wmma.Fill, Wmma.Store and
    // ConvertToSharedMemory in the kernel (lines 21, 24, 25 and 26): the whole call, once; Gpu.PopCount (line 27),
    // which the CPU prelude defines, is not reported.
    // mutant: src/Compiler/Kassyi.GSharp.Compiler/Diagnostics/Kernel/Rules/CallTargetRule.cs
    // Analyze(InvocationExpressionSyntax) `reportsUnsupportedIntrinsic: true` -> `false`
    // input: every call above
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task UnsupportedIntrinsic_CpuDialect_ReportsGsh0054AtCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        IReadOnlyList<(DiagnosticOrigin, int, int, int, int)> expected = dialect == "Cpu"
            ?
            [
                (Analyzer, 12, 13, 12, 44),
                (Analyzer, 13, 13, 13, 45),
                (Analyzer, 21, 13, 21, 45),
                (Analyzer, 24, 13, 24, 34),
                (Analyzer, 25, 13, 25, 72),
                (Analyzer, 26, 23, 26, 61)
            ]
            : [];
        Assert.Equal(expected, diagnostics.Ranges(GSharpDiagnostics.IntrinsicNotImplementedByDialect.Id));
    }

    // The method and the dialect
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task UnsupportedIntrinsic_DiagnosticMessage_NamesTheMethodAndTheDialect()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        var message = Assert.Single(
            diagnostics,
            static d => d.Id == GSharpDiagnostics.IntrinsicNotImplementedByDialect.Id && d.StartLine == 21).Message;
        Assert.Equal(
            "'Gpu.MBarrierInit' is not implemented by the 'StandardCppDialect' dialect; the call is replaced with a " +
            "runtime failure",
            message);
    }

    // The native CPU code calls no function that the CPU prelude leaves undefined: each call is the runtime failure,
    // which carries the method and the dialect to the exception of the launch. The CUDA body that the export of the
    // device function carries for a CUDA project (CudaBody) keeps the native names
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task UnsupportedIntrinsic_CpuDialect_WritesRuntimeFailureInsteadOfTheNativeName()
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(TestCase));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");
        var cpuOnly = string.Concat(run.Output.SyntaxTrees
            .Where(tree => !compilation.SyntaxTrees.Contains(tree))
            .Select(WithoutCudaBodies));

        Assert.Contains(
            "__gsharp_unsupported(\"\"Gpu.MBarrierInit is not implemented by the StandardCppDialect dialect\"\")",
            cpuOnly);
        Assert.Contains(
            "__gsharp_unsupported(\"\"Gpu.MBarrierArrive is not implemented by the StandardCppDialect dialect\"\")",
            cpuOnly);
        Assert.Contains(
            "__gsharp_unsupported(\"\"Wmma.Fill is not implemented by the StandardCppDialect dialect\"\")",
            cpuOnly);
        Assert.DoesNotContain("__gsharp_mbarrier_", cpuOnly);
        Assert.DoesNotContain("__gsharp_wmma_fill", cpuOnly);
        Assert.DoesNotContain("__gsharp_wmma_store", cpuOnly);
    }

    // The text of the tree without the CudaBody = @"<CUDA definition>" arguments of its device export attributes
    private static string WithoutCudaBodies(SyntaxTree tree)
    {
        var text = tree.ToString();
        var kept = new StringBuilder(text.Length);
        var start = 0;
        foreach (var body in tree.GetRoot().DescendantNodes().OfType<AttributeArgumentSyntax>()
                     .Where(static argument =>
                         argument.NameEquals?.Name.Identifier.ValueText == DeviceExportAttributeNames.Cuda.Body))
        {
            kept.Append(text.AsSpan(start, body.SpanStart - start));
            start = body.Span.End;
        }

        return kept.Append(text.AsSpan(start)).ToString();
    }
}
