namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class MemoryAndFixedArraysTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_FixedBufferKernel()
    {
        uint[] dataArray = [5, 0];
        AdvancedMemoryKernels.LaunchFixedBufferKernel(dataArray);

        Assert.Equal(15u, dataArray[1]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_SharedMemoryStaticKernel()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);

        int[] intOutput = [0];
        float[] floatOutput = [0.0f];
        CustomPoint[] pointOutput = [default];

        MemoryAndFixedArraysKernels.LaunchSharedMemoryStaticKernel(
            intOutput, floatOutput, pointOutput, blockDimX: 16);

        Assert.Equal(30, intOutput[0]);
        Assert.Equal(15.0f, floatOutput[0]);
        Assert.Equal(5.0f, pointOutput[0].X);
        Assert.Equal(10.0f, pointOutput[0].Y);
    }
}
