
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class MemoryAndFixedArraysTests
:  TranspilerTestBase{
        [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_FixedBufferKernel()
    {
        uint[] dataArray = [5, 0];
        AdvancedMemoryKernels.LaunchFixedBufferKernel(dataArray);

        Assert.Equal(15u, dataArray[1]);
    }
}
