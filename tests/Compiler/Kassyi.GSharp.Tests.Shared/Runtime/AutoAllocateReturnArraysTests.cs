using System;
using System.Linq;
using Xunit;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class AutoAllocateReturnArraysTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void NormalReturnKernel_MultiThread_ReturnsExpectedArray()
    {
        var result = AutoAllocateReturnArraysKernels.LaunchNormalReturnKernel(10, blockDimX: 5, blockDimY: 2);
        
        Assert.NotNull(result);
        Assert.Equal(10, result.Length);
        Assert.All(result, x => Assert.Equal(20.0f, x));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void OneThreadKernel_OneThread_ReturnsSingleElementArray()
    {
        var result = AutoAllocateReturnArraysKernels.LaunchOneThreadKernel(blockDimX: 1, blockDimY: 1, blockDimZ: 1);
        
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(42, result[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void StructReturnKernel_BlittableStruct_ReturnsExpectedStructArray()
    {
        var result = AutoAllocateReturnArraysKernels.LaunchStructReturnKernel(blockDimX: 100);
        
        Assert.NotNull(result);
        Assert.Equal(100, result.Length);
        for(int i = 0; i < 100; i++)
        {
            Assert.Equal(i, result[i].A);
            Assert.Equal(i * 1.5f, result[i].B);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void KernelLaunchZeroDimension_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            AutoAllocateReturnArraysKernels.LaunchNormalReturnKernel(10, blockDimX: 0));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void KernelLaunchOverflowingDimension_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => 
            AutoAllocateReturnArraysKernels.LaunchNormalReturnKernel(10, blockDimX: int.MaxValue, blockDimY: 2));
    }
}
