using System;
using System.Linq;
using Xunit;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Collection(TestCollections.GlobalStateSequential)]
public class AutoAllocateReturnArraysTests : TranspilerTestBase
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

    // #704: the device writes the returned elements in its layout; the launcher reads them back through it. Three
    // blocks of 4x2 threads, so that each element is the one of its global thread index, on every path (#889 review)
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void VectorResultKernel_LayoutsDiffer_ReturnsTheFieldsAtTheirPlaces(bool forceManaged)
    {
        var result = Run(forceManaged, () =>
            AutoAllocateReturnArraysKernels.LaunchVectorResultKernel(10f, gridDimX: 3, blockDimX: 4, blockDimY: 2));

        Assert.Equal(24, result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            float t = i * 10f;
            var r = result[i];
            Assert.Equal(
                new[] { t + 1f, t + 2f, t + 3f, t + 4f, t + 5f, t + 6f, t + 7f, t + 8f },
                new[] { r.A, r.V.X, r.V.Y, r.B, r.W.X, r.W.Y, r.W.Z, r.W.W });
        }
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoLayoutResultKernel_HostLayoutUnknown_ReturnsTheFieldsAtTheirPlaces(bool forceManaged)
    {
        var result = Run(forceManaged, () =>
            AutoAllocateReturnArraysKernels.LaunchAutoLayoutResultKernel(gridDimX: 3, blockDimX: 4, blockDimY: 2));

        Assert.Equal(24, result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            Assert.Equal(((byte)(i + 1), 1000L * i + 7L, (byte)(i + 100)), (result[i].A, result[i].B, result[i].C));
        }
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitUnionWithConvertedResultKernel_WidestUnionArmPreservesAllEightBytes(bool forceManaged)
    {
        var result = Run(forceManaged, () =>
            AutoAllocateReturnArraysKernels.LaunchExplicitUnionWithConvertedResultKernel(
                gridDimX: 3, blockDimX: 4, blockDimY: 2));

        Assert.Equal(24, result.Length);
        for (int i = 0; i < result.Length; i++)
        {
            long expectedRaw = 0x01020304_05060708L + i;
            Assert.Equal(expectedRaw, result[i].Raw);
            Assert.Equal( unchecked((int)expectedRaw), result[i].Low);
            Assert.Equal(100 + i, result[i].Converted.Head.A);
            Assert.Equal((byte)(i + 1), result[i].Converted.Tail);
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

    private T Run<T>(bool forceManaged, Func<T> launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            return launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
