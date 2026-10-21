using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public class ManagedExecutionTests : TranspilerTestBase
{
    [SkippableTheory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    [InlineData(256)]
    public void Test_Kernel_Direct_Managed_Execution_SharedMemoryAndSyncThreads(int length)
    {
        // 1. Force the engine to execute the Managed Cooperative Scheduler (skip native AOT DLL execution)
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        
        try
        {
            var input = new int[length];
            var output = new int[length];

            for (int i = 0; i < length; i++)
            {
                input[i] = i;
            }

            // 2. Invoke the kernel wrapper
            // If Managed Emission is correctly implemented, this will invoke __gsharp_launch_managed_ReverseArrayWithSharedMemoryKernel
            // which executes the state machine and shared memory in pure C#.
            SharedMemoryKernels.LaunchReverseArrayWithSharedMemoryKernel(
                input,
                output,
                length,
                blockDimX: 256 // Matches the shared memory static size in the kernel
            );

            // 3. Verify the output (array should be reversed correctly by cooperative threads using SharedMemory)
            for (int i = 0; i < length; i++)
            {
                Assert.Equal(length - 1 - i, output[i]);
            }
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }

    [SkippableFact]
    public void Test_Kernel_Direct_Managed_Execution_WarpShuffleReduceSum()
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        
        try
        {
            const int threadsPerWarp = 32;
            const int numWarps = 4;
            const int totalThreads = threadsPerWarp * numWarps;

            int[] input = new int[totalThreads];
            for (int i = 0; i < totalThreads; i++)
            {
                input[i] = i + 1;
            }

            int[] output = new int[numWarps];

            WarpShuffleKernels.LaunchWarpReductionKernel(
                input,
                output,
                blockDimX: (uint)totalThreads
            );

            for (int w = 0; w < numWarps; w++)
            {
                int expectedWarpSum = 0;
                for (int lane = 0; lane < threadsPerWarp; lane++)
                {
                    expectedWarpSum += input[w * threadsPerWarp + lane];
                }

                Assert.Equal(expectedWarpSum, output[w]);
            }
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
    [Fact]
    public void Test_ManagedGpuContext_DirectCalls_ForCoverage()
    {
        try
        {
            ManagedGpuContext.ClearThreadContext();
            ManagedGpuContext.SetThreadContext(new Kassyi.GSharp.Core.GpuThreadContext(
                0, 0, 0, 0, 0, 0, 32, 1, 1, 1, 1, 1
            ));

            ManagedGpuContext.InitializeGrid(1);

            // Test AllocateSharedMemory: the same declaration gets the same buffer, another declaration a separate one
            var span = ManagedGpuContext.AllocateSharedMemory<int>(1UL, 10);
            Assert.Equal(10, span.Length);
            span[0] = 42;
            Assert.Equal(42, ManagedGpuContext.AllocateSharedMemory<int>(1UL, 10)[0]);
            ManagedGpuContext.AllocateSharedMemory<int>(2UL, 10)[0] = 7;
            Assert.Equal(42, ManagedGpuContext.AllocateSharedMemory<int>(1UL, 10)[0]);

            // Negative case
            var emptySpan = ManagedGpuContext.AllocateSharedMemory<int>(3UL, 0);
            Assert.Equal(0, emptySpan.Length);

            // Test InitializeGrid array resize
            ManagedGpuContext.InitializeGrid(10);
            ManagedGpuContext.InitializeGrid(5);

            // Test warp shuffles directly
            ManagedGpuContext.__gsharp_warp_exchange_assign(42);
            
            var valUp = ManagedGpuContext.__gsharp_warp_shuffle_read_up_sync<object, int>(null, 0xFFFFFFFF, 0, 1, 32);
            var valDown = ManagedGpuContext.__gsharp_warp_shuffle_read_down_sync<object, int>(null, 0xFFFFFFFF, 0, 1, 32);
            var valXor = ManagedGpuContext.__gsharp_warp_shuffle_read_xor_sync<object, int>(null, 0xFFFFFFFF, 0, 1, 32);
            var valSync = ManagedGpuContext.__gsharp_warp_shuffle_read_sync<object, int>(null, 0xFFFFFFFF, 0, 1, 32);

            // Out of bounds cases
            ManagedGpuContext.__gsharp_warp_shuffle_read_up_sync<object, int>(null, 0xFFFFFFFF, 0, 33, 32);
            ManagedGpuContext.__gsharp_warp_shuffle_read_down_sync<object, int>(null, 0xFFFFFFFF, 0, 33, 32);
            ManagedGpuContext.__gsharp_warp_shuffle_read_xor_sync<object, int>(null, 0xFFFFFFFF, 0, 33, 32);
            ManagedGpuContext.__gsharp_warp_shuffle_read_sync<object, int>(null, 0xFFFFFFFF, 0, 33, 32);
        }
        finally
        {
            ManagedGpuContext.ClearThreadContext();
        }
    }
}
