namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class CollectionsAndMemoryTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_ForeachCollectionExpression_Execution()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachCollectionExpressionKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_ForeachStackAlloc_Execution()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachStackAllocKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_IndexAndRange_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<CollectionsAndMemoryTarget>(
            nameof(CollectionsAndMemoryTarget.IndexAndRange), dialect: CurrentDialect);

        // The analyzer reports the construct (UnsupportedSyntaxKindDiagnosticsTests); the transpiler records the failure and
        // writes a placeholder
        diagnostics.AssertFails(GSharpDiagnostics.UnsupportedSyntax, "'IndexExpression'");
        Assert.Contains("/*unsupported:IndexExpression*/", cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_SpanUsage_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<CollectionsAndMemoryTarget>(
            nameof(CollectionsAndMemoryTarget.SpanUsage), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("GSharpArray<int> span = data.AsSpan(2,4);", cudaSource.WithCSharpNames());
        Assert.Contains("GSharpArray<int> roSpan = data.AsSpan(0,2);", cudaSource.WithCSharpNames());
        Assert.Contains("span.length", cudaSource.WithCSharpNames());
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_MultidimensionalIndexer_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<CollectionsAndMemoryTarget>(
            nameof(CollectionsAndMemoryTarget.MultidimensionalIndexer), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.Contains("output[0]=flatGrid[row*width+col];", cudaSource.WithCSharpNames());
    }
}

public class CollectionsAndMemoryTarget
{
    public static void IndexAndRange(int[] data, int[] output)
    {
        output[0] = data[^1];
    }

    public static void SpanUsage(int[] data, int[] output)
    {
        Span<int> span = data.AsSpan(2, 4);
        ReadOnlySpan<int> roSpan = data.AsSpan(0, 2);
        output[0] = span[0] + roSpan[1] + span.Length;
    }

    public static void MultidimensionalIndexer(float[] flatGrid, int width, float[] output)
    {
        int row = 1;
        int col = 2;
        output[0] = flatGrid[row * width + col];
    }
}

