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

    // #704: the device writes the returned elements in its layout; the launcher reads them back through it
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void VectorResultKernel_LayoutsDiffer_ReturnsTheFieldsAtTheirPlaces()
    {
        var result = AutoAllocateReturnArraysKernels.LaunchVectorResultKernel(10f, blockDimX: 4);

        Assert.Equal(4, result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            float t = i * 10f;
            var r = result[i];
            Assert.Equal(
                new[] { t + 1f, t + 2f, t + 3f, t + 4f, t + 5f, t + 6f, t + 7f, t + 8f },
                new[] { r.A, r.V.X, r.V.Y, r.B, r.W.X, r.W.Y, r.W.Z, r.W.W });
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void AutoLayoutResultKernel_HostLayoutUnknown_ReturnsTheFieldsAtTheirPlaces()
    {
        var result = AutoAllocateReturnArraysKernels.LaunchAutoLayoutResultKernel(blockDimX: 3);

        Assert.Equal(3, result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            Assert.Equal(((byte)(i + 1), 1000L * i + 7L, (byte)(i + 100)), (result[i].A, result[i].B, result[i].C));
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
