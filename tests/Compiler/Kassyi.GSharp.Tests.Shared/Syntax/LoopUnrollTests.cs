using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class LoopUnrollTests : TranspilerTestBase
{
    private const string TestCasePath = "Analyzers/LoopUnrollStatements.cs";

    // The analyzers run once for the whole file; each case reads the unroll diagnostics inside its own method
    private static readonly Lazy<Task<ImmutableArray<Diagnostic>>> s_analyzerDiagnostics = new(GetAnalyzerDiagnosticsAsync);

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync()
    {
        var (_, analyzer, generator) =
            await KernelDiagnostics.GetDiagnosticsAsync(RoslynTestUtility.ReadTestCase(TestCasePath), "Cuda");
        Assert.DoesNotContain(generator, static d => IsUnrollDiagnostic(d.Id));
        return analyzer;
    }

    private static bool IsUnrollDiagnostic(string id) =>
        id == GSharpDiagnostics.UnrollHintMustPrecedeLoop.Id || id == GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant.Id;

    private static (string CudaSource, List<TranspileFailure> Failures) TranspileTestCaseMethod(string methodName)
    {
        var (compilation, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "LoopUnrollStatements.cs"));

        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<TranspileFailure>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), false,
            new GSharpCudaDialect(), out var _);
        return (cudaSource, diagnostics);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_LoopUnrollForKernel_Execution()
    {
        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollForKernel(data, blockDimX: 1);
        Assert.Equal(10, data[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_LoopUnrollFactorKernel_Execution()
    {
        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollFactorKernel(data, blockDimX: 1);
        Assert.Equal(100, data[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_LoopUnrollWhileKernel_Execution()
    {
        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollWhileKernel(data, blockDimX: 1);
        Assert.Equal(15, data[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_LoopUnrollNestedKernel_Execution()
    {
        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollNestedKernel(data, blockDimX: 1);
        Assert.Equal(18, data[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_LoopUnrollSingleStatementKernel_Execution()
    {
        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollSingleStatementKernel(data, blockDimX: 1);
        Assert.Equal(8, data[0]);
    }

    public static TheoryData<string> UnrollCaseNames()
    {
        var (_, tree, _) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "LoopUnrollStatements.cs"));
        var data = new TheoryData<string>();
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            data.Add(method.Identifier.Text);
        }

        return data;
    }

    /// <summary>
    /// Snapshots the transpiled output of every case in LoopUnrollStatements.cs and audits it against an
    /// oracle derived from the source's own <c>Gpu.Hint.Unroll</c> call sites (pragma text, placement, diagnostics).
    /// Adding a case to the test-case file automatically adds coverage.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnrollCaseNames))]
    [Category(TestCategory.Syntax)]
    public async Task Test_LoopUnroll_SnapshotAndAudit(string methodName)
    {
        var (_, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "LoopUnrollStatements.cs"));
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var (cudaSource, failures) = TranspileTestCaseMethod(methodName);

        // The analyzer reports a misplaced hint or a bad factor (S-46); the transpiler records the same IDs as failures
        var analyzerDiagnostics = (await s_analyzerDiagnostics.Value)
            .Where(d => IsUnrollDiagnostic(d.Id) && methodDecl.Span.Contains(d.Location.SourceSpan))
            .ToList();
        LoopUnrollAudit.Audit(methodDecl, semanticModel, cudaSource, analyzerDiagnostics);
        Assert.Equal(
            analyzerDiagnostics.Select(static d => d.Id).Order(StringComparer.Ordinal),
            failures.Select(static f => f.Descriptor.Id).Order(StringComparer.Ordinal));

        await Verify(cudaSource, $"LoopUnroll_{methodName}");
    }
}
