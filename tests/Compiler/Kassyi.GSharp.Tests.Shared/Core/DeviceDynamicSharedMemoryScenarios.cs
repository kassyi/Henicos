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

    // D1 (R1)
    public static void SuspendingHelperDoesNotOverlapCaller(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchSuspendingCallerKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + 2 * i, output[i]);
        }
    }

    // D8
    public static void HelperReachedFromTwoKernels(Action<Action> run)
    {
        var helperOnly = new int[Length];
        var helperAndCaller = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchHelperOnlyKernel(helperOnly, Length,
            blockDimX: Length, sharedMemBytes: Length * sizeof(int)));
        run(() => DeviceDynamicSharedMemoryKernels.LaunchHelperAndCallerKernel(helperAndCaller, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(2 * i, helperOnly[i]);
            Assert.Equal(i + 100 + 2 * i, helperAndCaller[i]);
        }
    }

    // D2 (R2)
    public static void PlainHelperDoesNotOverlapCaller(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchPlainCallerKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + 2 * i, output[i]);
        }
    }

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

    // D4 (R4)
    public static void PlainHelperPersistsAcrossCalls(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchPlainPersistKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal((i + 1) % Length + 1, output[i]);
        }
    }

    // D5 (R5)
    public static void StaticHelperPersistsAcrossCalls(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchStaticPersistKernel(output, blockDimX: Length));

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

    // D6 on the native CPU path: the helper's region is not the last one when it grows, which CUDA traps (#544, C10)
    public static void GrowingHelperAfterCallerDeclarationThrows(Action<Action> run)
    {
        const int smallCount = 8;
        var output = new int[Length];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            run(() => DeviceDynamicSharedMemoryKernels.LaunchGrowAfterCallerKernel(output, smallCount, Length,
                blockDimX: Length, sharedMemBytes: 3 * Length * sizeof(int))));

        Assert.Contains("G#: a shared memory declaration grew after a later declaration", exception.Message);
    }

    // D7
    public static void RecursiveHelperSharesOneBufferAndDoesNotOverlapCaller(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchRecursiveKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(2 * i + 121, output[i]);
        }
    }

    // D9 (R7)
    public static void NestedScopeDeclarationsDoNotOverlap(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchNestedScopeKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(2 * i + 1001, output[i]);
        }
    }

    // D10: on CUDA the kernel's 12 bytes end at 12; the helper's region starts at 16 and grows in place to 32 bytes
    public static void ZeroCountDeclarationGrowsInPlace(Action<Action> run)
    {
        const int smallCount = 3;
        const int largeCount = 4;
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchZeroThenGrowKernel(output, smallCount, largeCount,
            blockDimX: Length, sharedMemBytes: 32));

        for (int i = 0; i < Length; i++)
        {
            int expected = (i < smallCount ? i + 100 : 0) + (i < largeCount ? 2 * i : 0);
            Assert.Equal(expected, output[i]);
        }
    }

    // D11
    public static void HelperBehindFunctionWithoutDeclaration(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchMiddleCallerKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 2 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + 2 * i, output[i]);
        }
    }

    // D12
    public static void HelperWithoutParameters(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchNoParameterCallerKernel(output,
            blockDimX: Length, sharedMemBytes: Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(3 * i, output[i]);
        }
    }

    // D13
    public static void HelpersCalledFromLocalFunctionAndLambda(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceDynamicSharedMemoryKernels.LaunchNestedFunctionCallerKernel(output, Length,
            blockDimX: Length, sharedMemBytes: 3 * Length * sizeof(int)));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + 2 * i + 3 * i, output[i]);
        }
    }
}
