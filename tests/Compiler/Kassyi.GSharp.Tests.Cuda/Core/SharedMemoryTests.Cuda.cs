using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class SharedMemoryTests
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void MBarrier_SharedMemoryWrites_SynchronizesSharedMemoryWrites()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[32];

        SharedMemoryRefIndexerKernels.LaunchMBarrierSharedMemoryKernel(output, blockDimX: 32);

        for (int tid = 0; tid < 32; tid++)
        {
            Assert.Equal(3 * ((tid + 1) % 32), output[tid]);
        }
    }
}
