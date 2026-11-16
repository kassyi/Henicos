using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class DeviceDynamicSharedMemoryTests
{
    [SkippableTheory]
    [InlineData(-1)]
    [InlineData(-32)]
    [Scenario(TestScenario.SadPath)]
    public void NegativeCount_AfterRegionNative_ThrowsInvalidOperationException(int negative)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[DeviceDynamicSharedMemoryScenarios.Length];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceDynamicSharedMemoryKernels.LaunchNegativeCountKernel(output, DeviceDynamicSharedMemoryScenarios.Length,
                negative, blockDimX: DeviceDynamicSharedMemoryScenarios.Length,
                sharedMemBytes: 3 * DeviceDynamicSharedMemoryScenarios.Length * sizeof(int)));
        Assert.Contains("G#: the count of a shared memory declaration is negative", ex.Message);
        Assert.Null(ex.InnerException);
    }
}
