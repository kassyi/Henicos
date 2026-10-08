
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class LoopsAndJumpsTests
:  TranspilerTestBase{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_ForeachInlineArray_Execution()
    {
        int[] output = [0];
        LoopsAndJumpsKernels.LaunchForeachInlineArrayKernel(output, blockDimX: 1);
        Assert.Equal(100, output[0]);
    }

    [Fact()]
    [Category(TestCategory.Syntax)]
    public void Test_SwitchGotoCase_Transpilation()
    {
    }

    [Fact()]
    [Category(TestCategory.Syntax)]
    public void Test_NamedBreakGoto_Transpilation()
    {
    }
}
