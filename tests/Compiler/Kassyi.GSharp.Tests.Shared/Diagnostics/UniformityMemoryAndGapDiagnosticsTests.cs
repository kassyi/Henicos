using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The uniformity analysis records a write of a field or auto-property of an array, span or shared-memory element,
/// and a call of a mutating struct member on one, as a write of that element (#1272), and takes a gap of a
/// <c>Gpu.SyncGrid(body)</c> for a point where the threads that skip a divergent block barrier wait while the others
/// wait at the barrier (#982).
/// </summary>
[Category(TestCategory.Compiler)]
public class UniformityMemoryAndGapDiagnosticsTests
{
    private const string FieldWrites = "Diagnostics/ElementFieldWrites.cu.cs";
    private const string GapBarrier = "Diagnostics/SyncGridGapBarrier.cu.cs";

    // Written in part, then read under the condition of a barrier that the skipping threads meet again: GSH0083 at
    // the barrier, as for a write of the whole element (ElementKernel)
    [Theory]
    [InlineData("Cpu", "ArrayFieldKernel", 26)]
    [InlineData("Cpu", "CompoundFieldKernel", 39)]
    [InlineData("Cpu", "IncrementFieldKernel", 52)]
    [InlineData("Cpu", "NestedFieldKernel", 65)]
    [InlineData("Cpu", "SpanFieldKernel", 79)]
    [InlineData("Cpu", "SharedFieldKernel", 93)]
    [InlineData("Cpu", "ElementKernel", 137)]
    [InlineData("Cpu", "AutoPropertyKernel", 171)]
    [InlineData("Cpu", "MutatingMethodKernel", 185)]
    [InlineData("Cuda", "ArrayFieldKernel", 26)]
    [InlineData("Cuda", "CompoundFieldKernel", 39)]
    [InlineData("Cuda", "IncrementFieldKernel", 52)]
    [InlineData("Cuda", "NestedFieldKernel", 65)]
    [InlineData("Cuda", "SpanFieldKernel", 79)]
    [InlineData("Cuda", "SharedFieldKernel", 93)]
    [InlineData("Cuda", "ElementKernel", 137)]
    [InlineData("Cuda", "AutoPropertyKernel", 171)]
    [InlineData("Cuda", "MutatingMethodKernel", 185)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_PartOfAnElementWritten_ReportsGsh0083AtTheBarrier(string dialect, string kernel, int line)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(FieldWrites, dialect);

        Assert.Equal(
            [(Analyzer, line, 13, line, 30)],
            InKernel(diagnostics, FieldWrites, kernel, GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
        Assert.Empty(InKernel(diagnostics, FieldWrites, kernel, GSharpDiagnostics.BarrierAfterDivergentExit.Id));
    }

    // A barrier between the write and the read, a field of a local copy of the element, a readonly member called on
    // the element: nothing written since the last barrier, no diagnostic
    [Theory]
    [InlineData("Cpu", "FieldWriteThenBarrierKernel")]
    [InlineData("Cpu", "LocalFieldKernel")]
    [InlineData("Cpu", "ReadonlyMethodKernel")]
    [InlineData("Cuda", "FieldWriteThenBarrierKernel")]
    [InlineData("Cuda", "LocalFieldKernel")]
    [InlineData("Cuda", "ReadonlyMethodKernel")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ElementNotWrittenSinceTheBarrier_ReportsNothing(string dialect, string kernel)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(FieldWrites, dialect);

        Assert.Empty(InKernel(diagnostics, FieldWrites, kernel, GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
        Assert.Empty(InKernel(diagnostics, FieldWrites, kernel, GSharpDiagnostics.BarrierAfterDivergentExit.Id));
    }

    // The threads that skip the block barrier meet the others at the grid synchronization of the next gap: GSH0083,
    // not GSH0084 (no thread leaves the kernel)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarrierBeforeASyncGridGap_ReportsGsh0083(string dialect)
    {
        const string kernel = "DivergentBarrierBeforeGapKernel";
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(GapBarrier, dialect);

        Assert.Equal(
            [(Analyzer, 17, 17, 17, 34)],
            InKernel(diagnostics, GapBarrier, kernel, GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
        Assert.Empty(InKernel(diagnostics, GapBarrier, kernel, GSharpDiagnostics.BarrierAfterDivergentExit.Id));
    }

    // The divergent barrier in the last statement: no gap follows, the threads that skip it leave (GSH0084 at the
    // condition)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_DivergentBarrierInTheLastStatement_ReportsGsh0084(string dialect)
    {
        const string kernel = "DivergentBarrierInLastStatementKernel";
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(GapBarrier, dialect);

        Assert.Equal(
            [(Analyzer, 32, 17, 32, 34)],
            InKernel(diagnostics, GapBarrier, kernel, GSharpDiagnostics.BarrierAfterDivergentExit.Id));
        Assert.Empty(InKernel(diagnostics, GapBarrier, kernel, GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
    }

    // The ranges of id inside the declaration of kernel in the test case
    private static List<(DiagnosticOrigin, int, int, int, int)> InKernel(ImmutableArray<ReportedDiagnostic> diagnostics,
        string testCase, string kernel, string id)
    {
        var span = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(testCase)).SyntaxTrees.Last()
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == kernel)
            .GetLocation()
            .GetLineSpan()
            .Span;

        return diagnostics.Ranges(id)
            .Where(range => range.StartLine > span.Start.Line && range.StartLine <= span.End.Line + 1)
            .ToList();
    }
}
