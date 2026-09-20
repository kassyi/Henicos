
namespace Kassyi.GSharp.Tests.Syntax;

public class GEN29_StructMemoryLayoutTests:  TranspilerTestBase{

    [SkippableFact]
    [Trait("FeatureId", "GEN-29")]
    [Trait("Category", "GpuExecution")]
    public void Test_GEN29_SharedMemory_StaticExpansion_And_MultipleTypes()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        EnsureCapability(DialectCapabilities.SharedMemory);
        int[] intOutput = [0];
        float[] floatOutput = [0.0f];
        CustomPoint[] pointOutput = [new CustomPoint()];

        SYN_13_GSH_04_GEN_29_MemoryAndFixedArraysKernelsWrapper.LaunchSharedMemoryStaticKernel(
            intOutput, floatOutput, pointOutput, blockDimX: 256);

        Assert.Equal(30, intOutput[0]);
        Assert.Equal(15.0f, floatOutput[0]);
        Assert.Equal(5.0f, pointOutput[0].X);
        Assert.Equal(10.0f, pointOutput[0].Y);
    }
}
