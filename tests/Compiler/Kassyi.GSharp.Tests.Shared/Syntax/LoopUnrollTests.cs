using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class LoopUnrollTests : TranspilerTestBase
{


    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileTestCaseMethod(string methodName)
    {
        var (compilation, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "LoopUnrollStatements.cs"));

        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<Diagnostic>();
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

        var (cudaSource, diagnostics) = TranspileTestCaseMethod(methodName);

        LoopUnrollAudit.Audit(methodDecl, semanticModel, cudaSource, diagnostics);

        var report = cudaSource + string.Concat(diagnostics.Select(d => $"\n// {d.Id}"));
        await Verify(report, $"LoopUnroll_{methodName}");
    }
}
