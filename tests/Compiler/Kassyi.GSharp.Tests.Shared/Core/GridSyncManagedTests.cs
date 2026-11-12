using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GridSyncManagedTests : TranspilerTestBase
{
    [SkippableFact]
    public void GlobalBarrier_DirectManagedExecution_ProducesExpectedSequentialValues()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            int numBlocks = 4;
            int threadsPerBlock = 32;
            int totalThreads = numBlocks * threadsPerBlock;

            int[] buffer = new int[totalThreads];

            GridSyncManagedKernels.LaunchGlobalBarrierKernel(
                buffer,
                gridDimX: (uint)numBlocks,
                blockDimX: (uint)threadsPerBlock
            );

            // Assert
            for (int i = 0; i < totalThreads; i++)
            {
                int nextId = (i + 1) % totalThreads;
                int expected = nextId + 1;
                Assert.Equal(expected, buffer[i]);
            }
        }
    }

    [SkippableFact]
    public void GlobalReduction_DirectManagedExecution_ProducesExpectedSum()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            int numBlocks = 4;
            int threadsPerBlock = 256;
            int totalThreads = numBlocks * threadsPerBlock;

            int[] input = new int[totalThreads];
            int expectedSum = 0;
            for (int i = 0; i < totalThreads; i++)
            {
                input[i] = i + 1;
                expectedSum += input[i];
            }

            int[] blockSums = new int[numBlocks];
            int[] totalSum = new int[1];

            GridSyncManagedKernels.LaunchGlobalReductionKernel(
                input,
                blockSums,
                totalSum,
                gridDimX: (uint)numBlocks,
                blockDimX: (uint)threadsPerBlock
            );

            Assert.Equal(expectedSum, totalSum[0]);
        }
    }
}
