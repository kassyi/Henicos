using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Trait("Category", "GpuExecution")]
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
}
