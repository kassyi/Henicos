using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class MEM_05_UnmanagedBoundariesTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-05")]
    public void Test_MEM05_SafeStackallocKernel()
    {
        int[] output = new int[1];
        MEM_05_UnmanagedBoundariesKernelsWrapper.Launch_SafeStackallocKernel(output, blockDimX: 1);
        Assert.Equal(10, output[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-05")]
    public void Test_MEM05_SafeMemoryPinningKernel()
    {
        byte[] input = [10, 20, 30, 40];
        int[] output = new int[4];
        MEM_05_UnmanagedBoundariesKernelsWrapper.Launch_SafeMemoryPinningKernel(input, output, blockDimX: 4);
        Assert.Equal(10, output[0]);
        Assert.Equal(20, output[1]);
        Assert.Equal(30, output[2]);
        Assert.Equal(40, output[3]);
    }
}
