
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class CollectionsAndMemoryTests
    : TranspilerTestBase
{
    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_IndexAndRange_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_ForeachCollectionExpression_Execution()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachCollectionExpressionKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_ForeachStackAlloc_Execution()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachStackAllocKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_SpanUsage_Transpilation()
    {
    }

    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_MultidimensionalIndexer_Transpilation()
    {
    }
}
