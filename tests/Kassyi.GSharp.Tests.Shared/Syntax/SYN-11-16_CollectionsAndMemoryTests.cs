
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_11_16_CollectionsAndMemoryTests
    : TranspilerTestBase
{
    [SkippableFact]
    [Trait("FeatureId", "SYN-11")]
    [Trait("Category", "Syntax")]
    public void Test_SYN11_IndexAndRange_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-12")]
    [Trait("Category", "GpuExecution")]
    public void Test_SYN12_ForeachCollectionExpression_Execution()
    {
        int[] output = [0];
        SYN_70_72_LoopsAndJumpsKernelsWrapper.LaunchForeachCollectionExpressionKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "GpuExecution")]
    public void Test_SYN13_ForeachStackAlloc_Execution()
    {
        int[] output = [0];
        SYN_70_72_LoopsAndJumpsKernelsWrapper.LaunchForeachStackAllocKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-15")]
    [Trait("Category", "Syntax")]
    public void Test_SYN15_SpanUsage_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-16")]
    [Trait("Category", "Syntax")]
    public void Test_SYN16_MultidimensionalIndexer_Transpilation()
    {
    }
}
