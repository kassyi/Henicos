using Kassyi.GSharp.Tests.Helpers;
using System;
using System.Linq;
using Xunit;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Runtime;

public class GEN_08_AutoAllocateReturnArraysTests : TranspilerTestBase
{
    [SkippableFact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "GpuExecution")]
    public void NormalReturnKernel_MultiThread_ReturnsExpectedArray()
    {
        var result = GEN_08_AutoAllocateReturnArraysKernelsWrapper.LaunchNormalReturnKernel(10, blockDimX: 5, blockDimY: 2);
        
        Assert.NotNull(result);
        Assert.Equal(10, result.Length);
        Assert.All(result, x => Assert.Equal(20.0f, x));
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "GpuExecution")]
    public void OneThreadKernel_OneThread_ReturnsSingleElementArray()
    {
        var result = GEN_08_AutoAllocateReturnArraysKernelsWrapper.LaunchOneThreadKernel(blockDimX: 1, blockDimY: 1, blockDimZ: 1);
        
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(42, result[0]);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "GpuExecution")]
    public void StructReturnKernel_BlittableStruct_ReturnsExpectedStructArray()
    {
        var result = GEN_08_AutoAllocateReturnArraysKernelsWrapper.LaunchStructReturnKernel(blockDimX: 100);
        
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
    public void KernelLaunchZeroDimension_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            GEN_08_AutoAllocateReturnArraysKernelsWrapper.LaunchNormalReturnKernel(10, blockDimX: 0));
    }

    [Fact]
    [Trait("FeatureId", "GEN-08")]
    [Trait("Category", "SadPath")]
    public void KernelLaunchOverflowingDimension_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => 
            GEN_08_AutoAllocateReturnArraysKernelsWrapper.LaunchNormalReturnKernel(10, blockDimX: int.MaxValue, blockDimY: 2));
    }
}
