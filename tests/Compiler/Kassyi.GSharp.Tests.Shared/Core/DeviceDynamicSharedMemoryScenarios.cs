using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Launches and checks the kernels of <see cref="DeviceDynamicSharedMemoryKernels"/>. The native and managed test
/// classes share these scenarios and differ only in how a launch runs.
/// </summary>
internal static class DeviceDynamicSharedMemoryScenarios
{
    public const int Length = 32;

    // D3 (R4s)
    public static void SuspendingHelperPersistsAcrossCalls(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchSuspendingPersistKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal((i + 1) % Length + 1, output[i]);
        }
    }

    // D6 (R6)
    public static void GrowingHelperAfterCallerDeclaration(Action<Action> run)
    {
        const int smallCount = 8;
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchGrowAfterCallerKernel(output, smallCount, Length,
            blockDimX: Length, sharedMemBytes: 3 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + 2 * i, output[i]);
        }
    }
}
