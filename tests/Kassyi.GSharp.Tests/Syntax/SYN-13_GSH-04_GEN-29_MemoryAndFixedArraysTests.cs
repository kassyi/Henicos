using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_13_GSH_04_GEN_29_MemoryAndFixedArraysTests
{
    [Fact(Skip = "未実装: SYN-13 / GSH-04 (stackalloc トランスパイル機能)")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Execution")]
    public void Test_SYN13_GSH04_StackAlloc_HappyPath()
    {
    }

    [Fact(Skip = "未実装: SYN-13 / GSH-04 (stackalloc トランスパイル機能)")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Execution")]
    public void Test_SYN13_GSH04_StackAlloc_InTightLoop()
    {
    }

    [Fact(Skip = "未実装: SYN-13 / GSH-04 (stackalloc トランスパイル機能)")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Execution")]
    public void Test_SYN13_GSH04_StackAlloc_AsMethodArgument()
    {
    }

    [Fact]
    [Trait("FeatureId", "GEN-29")]
    [Trait("Category", "Execution")]
    public void Test_GEN29_SharedMemory_StaticExpansion_And_MultipleTypes()
    {
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
