
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_31_38_AdvancedMemoryTests
:  TranspilerTestBase{

    [SkippableFact]
    [Trait("FeatureId", "SYN-31")]
    [Trait("Category", "Syntax")]
    public void Test_SYN31_UnsafeAsPointer_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-32")]
    [Trait("Category", "Syntax")]
    public void Test_SYN32_MemoryMarshalCast_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-33")]
    [Trait("Category", "Syntax")]
    public void Test_SYN33_RefFields_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-34")]
    [Trait("Category", "Syntax")]
    public void Test_SYN34_SimdVector128_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-36")]
    public void Test_SYN36_PointerIncrementKernel()
    {
        uint[] src = [10, 20, 30];
        uint[] dest = [0, 0, 0];

        SYN_31_38_AdvancedMemoryKernelsWrapper.LaunchPointerIncrementKernel(src, dest);

        Assert.Equal(10u, dest[0]);
        Assert.Equal(20u, dest[1]);
        Assert.Equal(30u, dest[2]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-36")]
    public void Test_SYN36_PointerDecrementKernel()
    {
        uint[] src = [100, 200, 300];
        uint[] dest = [0, 0, 0];

        SYN_31_38_AdvancedMemoryKernelsWrapper.LaunchPointerDecrementKernel(src, dest);

        Assert.Equal(100u, dest[0]);
        Assert.Equal(200u, dest[1]);
        Assert.Equal(300u, dest[2]);
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-36")]
    [InlineData(new uint[] { 2, 0, 1 }, new uint[] { 5, 15, 25 }, new uint[] { 25, 35, 15 })]
    [InlineData(new uint[] { 0, 1, 2 }, new uint[] { 1, 2, 3 }, new uint[] { 11, 12, 13 })]
    [InlineData(new uint[] { 0, 1, 2 }, new uint[] { 0, 1, uint.MaxValue }, new uint[] { 10, 11, 9 })]
    [InlineData(new uint[] { 1, 2, 0 }, new uint[] { 0x55555555u, 0xAAAAAAAAu, 0x12345678u }, new uint[] { 0x12345682u, 0x5555555Fu, 0xAAAAAAB4u })]
    [InlineData(new uint[] { 2, 1, 0 }, new uint[] { 0, 0, 0 }, new uint[] { 10, 10, 10 })]
    public void Test_SYN36_PointerComplexAssignmentKernel(uint[] idx, uint[] val, uint[] expectedDest)
    {
        uint[] dest = [0, 0, 0];

        SYN_31_38_AdvancedMemoryKernelsWrapper.LaunchPointerComplexAssignmentKernel(idx, val, dest);

        Assert.Equal(expectedDest[0], dest[0]);
        Assert.Equal(expectedDest[1], dest[1]);
        Assert.Equal(expectedDest[2], dest[2]);
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-37")]
    [Trait("Category", "Syntax")]
    public void Test_SYN37_UnsafePointerArithmetic_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-38")]
    [Trait("Category", "Syntax")]
    public void Test_SYN38_FixedSizeBuffers_Transpilation()
    {
    }
}
