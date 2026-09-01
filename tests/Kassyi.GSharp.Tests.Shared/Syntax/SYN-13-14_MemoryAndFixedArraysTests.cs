
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_13_14_MemoryAndFixedArraysTests
:  TranspilerTestBase{

        [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-14")]
    public void Test_SYN14_FixedBufferKernel()
    {
        uint[] dataArray = [5, 0];
        SYN_31_38_AdvancedMemoryKernelsWrapper.Launch_FixedBufferKernel(dataArray);

        Assert.Equal(15u, dataArray[1]);
    }
}
