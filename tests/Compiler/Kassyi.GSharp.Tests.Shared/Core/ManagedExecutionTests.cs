using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Syntax;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public class ManagedExecutionTests : TranspilerTestBase
{
    [SkippableTheory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    [InlineData(256)]
    public void LaunchSharedMemoryAndSyncThreads_DirectManagedExecution_ReversesTheArray(int length)
    {
        // 1. Force the engine to execute the Managed Cooperative Scheduler (skip native AOT DLL execution)
        using (GpuExecution.Use(KernelExecution.Managed))
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
    }

    [SkippableFact]
    public void LaunchWarpShuffleReduceSum_DirectManagedExecution_ProducesExpectedSum()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
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
    }
    [Fact]
    public void ManagedGpuContext_DirectMethodCalls_ExecutesWithoutException()
    {
        try
        {
            ManagedThreadContext.Clear();
            ManagedThreadContext.Set(new Kassyi.GSharp.Core.GpuThreadContext(
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

            // Test the warp collectives directly: lane 0 meets alone (mask 1) and reads its own value; reading lane 1,
            // which never published, is an error
            var frame = new DirectWarpFrame();
            ManagedGpuContext.ResetWarpExchange();
            Assert.True(ManagedGpuContext.WarpCanRun(0, 1));
            ManagedGpuContext.__gsharp_warp_publish(0, ref frame, 0, 1u, 42, "Warp.ShuffleSync", "ManagedExecutionTests");
            Assert.False(ManagedGpuContext.WarpCanRun(0, 1));
            Assert.True(ManagedGpuContext.WarpCanRun(0, 2));
            Assert.Equal(42, ManagedGpuContext.__gsharp_warp_shuffle_read_sync(0, ref frame, 0, 1u, 0, 0, 32, "ManagedExecutionTests"));
            Assert.Equal(7, ManagedGpuContext.__gsharp_warp_shuffle_read_up_sync(0, ref frame, 0, 1u, 7, 1u, 32, "ManagedExecutionTests"));
            var notTakingPart = Assert.Throws<InvalidOperationException>(() =>
                ManagedGpuContext.__gsharp_warp_shuffle_read_down_sync(0, ref frame, 0, 1u, 0, 1u, 32, "ManagedExecutionTests"));
            Assert.Contains("reads lane 1 of warp 0, which does not take part", notTakingPart.Message);
            ManagedGpuContext.MarkWarpLaneExited(1);
            var exited = Assert.Throws<InvalidOperationException>(() =>
                ManagedGpuContext.__gsharp_warp_shuffle_read_xor_sync(0, ref frame, 0, 1u, 0, 1, 32, "ManagedExecutionTests"));
            Assert.Contains("reads lane 1 of warp 0, which has exited", exited.Message);
        }
        finally
        {
            ManagedThreadContext.Clear();
        }
    }

    // A frame of one warp collective, for the direct calls
    private sealed class DirectWarpFrame : IManagedWarpFrame
    {
        private uint _visits;

        public uint WarpPath => 0u;

        public uint AdvanceWarpVisit(int site) => ++_visits;

        public uint GetWarpVisit(int site) => _visits;
    }
}
