using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class SharedMemoryTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MBarrier_CpuDialectLaunch_ThrowsNotSupportedException()
    {
        var output = new int[32];

        Assert.Throws<NotSupportedException>(() =>
            SharedMemoryRefIndexerKernels.LaunchMBarrierSharedMemoryKernel(output, blockDimX: 32));
    }
}
