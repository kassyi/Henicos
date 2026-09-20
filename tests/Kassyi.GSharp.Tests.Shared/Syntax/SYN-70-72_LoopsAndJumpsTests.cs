
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_70_72_LoopsAndJumpsTests
:  TranspilerTestBase{
    [SkippableFact]
    [Trait("FeatureId", "SYN-70")]
    [Trait("Category", "GpuExecution")]
    public void Test_SYN70_ForeachInlineArray_Execution()
    {
        int[] output = [0];
        SYN_70_72_LoopsAndJumpsKernelsWrapper.LaunchForeachInlineArrayKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [Fact()]
    [Trait("FeatureId", "SYN-71")]
    [Trait("Category", "Syntax")]
    public void Test_SYN71_SwitchGotoCase_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-72")]
    [Trait("Category", "Syntax")]
    public void Test_SYN72_NamedBreakGoto_Transpilation()
    {
    }
}
