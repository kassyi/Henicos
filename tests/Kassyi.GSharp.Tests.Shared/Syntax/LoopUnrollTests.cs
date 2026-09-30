using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class LoopUnrollTests : TranspilerTestBase
{
    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileKernelMethod(string methodName)
    {
        var testCasePath = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "..", "Syntax", "LoopUnrollKernels.cu.cs");
        var compilation = MegaCompilationCache.GetDynamicCompilation(testCasePath);

        var tree = compilation.SyntaxTrees.First(t => t.FilePath == testCasePath);
        var semanticModel = compilation.GetSemanticModel(tree);

        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: false);
        return (cudaSource, diagnostics);
    }

    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileTestCaseMethod(string methodName)
    {
        var testCasePath = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Analyzers", "LoopUnrollStatements.cs");
        var compilation = MegaCompilationCache.GetDynamicCompilation(testCasePath);

        var tree = compilation.SyntaxTrees.First(t => t.FilePath == testCasePath);
        var semanticModel = compilation.GetSemanticModel(tree);

        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: false);
        return (cudaSource, diagnostics);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_LoopUnrollForKernel_Execution()
    {

        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollForKernel(data, blockDimX: 1);
        Assert.Equal(10, data[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_LoopUnrollFactorKernel_Execution()
    {

        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollFactorKernel(data, blockDimX: 1);
        Assert.Equal(100, data[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_LoopUnrollWhileKernel_Execution()
    {

        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollWhileKernel(data, blockDimX: 1);
        Assert.Equal(15, data[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_LoopUnrollNestedKernel_Execution()
    {

        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollNestedKernel(data, blockDimX: 1);
        Assert.Equal(18, data[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_LoopUnrollSingleStatementKernel_Execution()
    {

        int[] data = [0];
        LoopUnrollKernels.LaunchLoopUnrollSingleStatementKernel(data, blockDimX: 1);
        Assert.Equal(8, data[0]);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnrollFor_TranspilesPragmaUnroll()
    {
        var (cudaSource1, diagnostics1) = TranspileKernelMethod("LoopUnrollForKernel");
        Assert.Empty(diagnostics1);
        Assert.Contains("#pragma unroll", cudaSource1);
        Assert.Contains("#pragma unroll\nfor", cudaSource1.Replace("\r", ""));

        var (cudaSource2, diagnostics2) = TranspileTestCaseMethod("StatementUnrollFor");
        Assert.Empty(diagnostics2);
        Assert.Contains("#pragma unroll", cudaSource2);
        Assert.Contains("#pragma unroll\nfor", cudaSource2.Replace("\r", ""));
    }

    [Theory]
    [InlineData("StatementUnrollFactor0", "#pragma unroll\nfor")]
    [InlineData("StatementUnrollFactor1", "#pragma unroll 1\nfor")]
    [InlineData("StatementUnrollFactor", "#pragma unroll 4\nfor")]
    [InlineData("StatementUnrollFactorMax", "#pragma unroll 2147483647\nfor")]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnrollFactor_BoundaryTheory(string methodName, string expectedCudaPattern)
    {
        var (cudaSource, diagnostics) = TranspileTestCaseMethod(methodName);
        Assert.Empty(diagnostics);
        Assert.Contains(expectedCudaPattern, cudaSource.Replace("\r", ""));
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnrollFactor_TranspilesPragmaUnrollFactor()
    {
        var (cudaSource1, diagnostics1) = TranspileKernelMethod("LoopUnrollFactorKernel");
        Assert.Empty(diagnostics1);
        Assert.Contains("#pragma unroll 4", cudaSource1);
        Assert.Contains("#pragma unroll 4\nfor", cudaSource1.Replace("\r", ""));

        var (cudaSource2, diagnostics2) = TranspileTestCaseMethod("StatementUnrollFactor");
        Assert.Empty(diagnostics2);
        Assert.Contains("#pragma unroll 4", cudaSource2);
        Assert.Contains("#pragma unroll 4\nfor", cudaSource2.Replace("\r", ""));
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnroll_NegativeFactor_ReportsDiagnostic()
    {
        var (cudaSource, diagnostics) = TranspileTestCaseMethod("StatementUnrollNegativeFactor");
        Assert.Contains(diagnostics, d => d.Id == "GSH0016");
        Assert.DoesNotContain("#pragma unroll", cudaSource);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnroll_VariableFactor_ReportsDiagnostic()
    {
        var (cudaSource, diagnostics) = TranspileTestCaseMethod("StatementUnrollVariableFactor");
        Assert.Contains(diagnostics, d => d.Id == "GSH0016");
        Assert.DoesNotContain("#pragma unroll", cudaSource);
    }

    [Theory]
    [InlineData("StatementUnrollMisplacedEnd")]
    [InlineData("StatementUnrollMisplacedLast")]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnroll_MisplacedHint_ReportsDiagnostic(string methodName)
    {
        var (cudaSource, diagnostics) = TranspileTestCaseMethod(methodName);
        Assert.Contains(diagnostics, d => d.Id == "GSH0017");
        Assert.DoesNotContain("#pragma unroll", cudaSource);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnrollWhile_TranspilesPragmaUnroll()
    {
        var (cudaSource1, diagnostics1) = TranspileKernelMethod("LoopUnrollWhileKernel");
        Assert.Empty(diagnostics1);
        Assert.Contains("#pragma unroll", cudaSource1);
        Assert.Contains("#pragma unroll\nwhile", cudaSource1.Replace("\r", ""));

        var (cudaSource2, diagnostics2) = TranspileTestCaseMethod("StatementUnrollWhile");
        Assert.Empty(diagnostics2);
        Assert.Contains("#pragma unroll", cudaSource2);
        Assert.Contains("#pragma unroll\nwhile", cudaSource2.Replace("\r", ""));
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnrollNested_TranspilesPragmaUnrollOnInnerLoopOnly()
    {
        var (cudaSource, diagnostics) = TranspileTestCaseMethod("StatementUnrollNested");
        Assert.Empty(diagnostics);
        var normalized = cudaSource.Replace("\r", "");
        Assert.Contains("#pragma unroll", normalized);

        int index1 = normalized.IndexOf("#pragma unroll", StringComparison.Ordinal);
        int index2 = normalized.IndexOf("#pragma unroll", index1 + 1, StringComparison.Ordinal);
        Assert.Equal(-1, index2);

        int outerForIndex = normalized.IndexOf("for(int i = 0;", StringComparison.Ordinal);
        Assert.True(outerForIndex >= 0 && outerForIndex < index1, "Outer loop 'for' should precede #pragma unroll");

        int innerForIndex = normalized.IndexOf("for(int j = 0;", StringComparison.Ordinal);
        Assert.True(innerForIndex > index1, "Inner loop 'for' should follow #pragma unroll");
    }

    [Fact]
    [Trait("Category", "Syntax")]
    public void Test_LoopUnrollSingleStatement_TranspilesPragmaUnroll()
    {
        var (cudaSource1, diagnostics1) = TranspileKernelMethod("LoopUnrollSingleStatementKernel");
        Assert.Empty(diagnostics1);
        Assert.Contains("#pragma unroll\nfor", cudaSource1.Replace("\r", ""));

        var (cudaSource2, diagnostics2) = TranspileTestCaseMethod("StatementUnrollSingleStatement");
        Assert.Empty(diagnostics2);
        Assert.Contains("#pragma unroll\nfor", cudaSource2.Replace("\r", ""));
    }
}
