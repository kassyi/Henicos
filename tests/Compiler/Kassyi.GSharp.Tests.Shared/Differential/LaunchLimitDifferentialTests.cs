#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Boundary and sad-path launch limit verification for kernel launch configurations.
/// Hardware upper-limit validation (gridDimY &gt; 65535, blockDimZ &gt; 64,
/// ThreadsPerBlock &gt; 1024, MaxCpuThreads, and void-kernel zero-dimension checks)
/// is tracked under #799.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class LaunchLimitDifferentialTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, 1u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 0u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 0u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 0u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 0u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 1u, 0u)]
    public void ZeroDimensionLaunch_AutoResultKernel_ThrowsArgumentOutOfRangeException(
        uint gridX,
        uint gridY,
        uint gridZ,
        uint blockX,
        uint blockY,
        uint blockZ)
    {
        int[] src = [1, 2, 3, 4];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchShapeDifferentialKernels.LaunchAutoResultThreeDimensionalKernel(
                src,
                salt: 42,
                gridDimX: gridX,
                gridDimY: gridY,
                gridDimZ: gridZ,
                blockDimX: blockX,
                blockDimY: blockY,
                blockDimZ: blockZ));

#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LaunchShapeDifferentialKernels.LaunchAutoResultThreeDimensionalKernel(
                    src,
                    salt: 42,
                    gridDimX: gridX,
                    gridDimY: gridY,
                    gridDimZ: gridZ,
                    blockDimX: blockX,
                    blockDimY: blockY,
                    blockDimZ: blockZ));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif
    }
}
