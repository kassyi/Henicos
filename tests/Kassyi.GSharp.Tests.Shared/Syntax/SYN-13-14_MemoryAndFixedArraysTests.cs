
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_13_14_MemoryAndFixedArraysTests
:  TranspilerTestBase{
        [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-14")]
    public void Test_SYN14_FixedBufferKernel()
    {
        uint[] dataArray = [5, 0];
        SYN_31_38_AdvancedMemoryKernels.LaunchFixedBufferKernel(dataArray);

        Assert.Equal(15u, dataArray[1]);
    }
}
