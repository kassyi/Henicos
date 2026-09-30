
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class StructMemoryLayoutTests:  TranspilerTestBase{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_SharedMemory_StaticExpansion_And_MultipleTypes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        int[] intOutput = [0];
        float[] floatOutput = [0.0f];
        CustomPoint[] pointOutput = [new CustomPoint()];

        MemoryAndFixedArraysKernels.LaunchSharedMemoryStaticKernel(
            intOutput, floatOutput, pointOutput, blockDimX: 256);

        Assert.Equal(30, intOutput[0]);
        Assert.Equal(15.0f, floatOutput[0]);
        Assert.Equal(5.0f, pointOutput[0].X);
        Assert.Equal(10.0f, pointOutput[0].Y);
    }
}
