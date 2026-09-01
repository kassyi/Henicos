
namespace Kassyi.GSharp.Tests.Syntax;

public class GEN29_StructMemoryLayoutTests:  TranspilerTestBase{

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("FeatureId", "GEN-29")]
    [Trait("Category", "Execution")]
    public void Test_GEN29_SharedMemory_StaticExpansion_And_MultipleTypes(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Explicit AI Judgment: Tests CUDA runtime/hardware specifics (e.g., SharedMemory, JIT, PTX, cuCtx).");
        int[] intOutput = [0];
        float[] floatOutput = [0.0f];
        CustomPoint[] pointOutput = [new CustomPoint()];

        SYN_13_GSH_04_GEN_29_MemoryAndFixedArraysKernelsWrapper.Launch_SharedMemoryStaticKernel(
            intOutput, floatOutput, pointOutput, blockDimX: 256);

        Assert.Equal(30, intOutput[0]);
        Assert.Equal(15.0f, floatOutput[0]);
        Assert.Equal(5.0f, pointOutput[0].X);
        Assert.Equal(10.0f, pointOutput[0].Y);
    }
}
