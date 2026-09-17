using System;
using System.Linq;
using Xunit;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Runtime;

public class GEN_08_AutoAllocateReturnArraysTests
{
    [Fact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "Runtime")]
    public void NormalReturnKernel_MultiThread_ReturnsExpectedArray()
    {
        var result = GEN_08_AutoAllocateReturnArraysKernelsWrapper.Launch_NormalReturnKernel(10, blockDimX: 5, blockDimY: 2);
        
        Assert.NotNull(result);
        Assert.Equal(10, result.Length);
        Assert.All(result, x => Assert.Equal(20.0f, x));
    }

    [Fact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "Runtime")]
    public void OneThreadKernel_OneThread_ReturnsSingleElementArray()
    {
        var result = GEN_08_AutoAllocateReturnArraysKernelsWrapper.Launch_OneThreadKernel(blockDimX: 1, blockDimY: 1, blockDimZ: 1);
        
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(42, result[0]);
    }

    [Fact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "Runtime")]
    public void StructReturnKernel_BlittableStruct_ReturnsExpectedStructArray()
    {
        var result = GEN_08_AutoAllocateReturnArraysKernelsWrapper.Launch_StructReturnKernel(blockDimX: 100);
        
        Assert.NotNull(result);
        Assert.Equal(100, result.Length);
        for(int i = 0; i < 100; i++)
        {
            Assert.Equal(i, result[i].A);
            Assert.Equal(i * 1.5f, result[i].B);
        }
    }

    [Fact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "SadPath")]
    public void KernelLaunch_ZeroDimension_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            GEN_08_AutoAllocateReturnArraysKernelsWrapper.Launch_NormalReturnKernel(10, blockDimX: 0));
    }

    [Fact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "SadPath")]
    public void KernelLaunch_OverflowingDimension_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => 
            GEN_08_AutoAllocateReturnArraysKernelsWrapper.Launch_NormalReturnKernel(10, blockDimX: int.MaxValue, blockDimY: 2));
    }
}
