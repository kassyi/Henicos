using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Launches and checks the kernels of <see cref="SharedMemoryRegionKernels"/>. The native and managed test classes
/// share these scenarios and differ only in how a launch runs.
/// </summary>
internal static class SharedMemoryRegionScenarios
{
    public const int Length = 64;

    public static void LaterDeclarationKeepsEarlierBuffer(Action<Action> run)
    {
        var output = new int[Length];

        run(() => SharedMemoryRegionKernels.LaunchStaleSpanKernel(output, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal((i + 1) % Length + 1, output[i]);
        }
    }

    public static void MixedElementTypes(Action<Action> run)
    {
        var output = new int[Length];

        run(() => SharedMemoryRegionKernels.LaunchMixedElementTypesKernel(output, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            int next = (i + 1) % Length;
            Assert.Equal(3 * next + 3, output[i]);
        }
    }

    public static void GrowingDeclaration(Action<Action> run)
    {
        const int smallCount = 16;
        const int largeCount = 32;
        var output = new int[largeCount];

        // CUDA sizes dynamic shared memory per launch: the larger call must fit
        run(() => SharedMemoryRegionKernels.LaunchGrowingDeclarationKernel(output, smallCount, largeCount,
            blockDimX: largeCount, sharedMemBytes: largeCount * sizeof(int)));

        for (int i = 0; i < largeCount; i++)
        {
            int small = i < smallCount ? (i + 1) % smallCount + 1 : 0;
            int large = (i + 1) % largeCount + 1;
            Assert.Equal(small * 100 + large, output[i]);
        }
    }
}
