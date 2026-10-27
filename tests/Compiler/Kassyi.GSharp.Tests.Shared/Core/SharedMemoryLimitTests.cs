#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemoryLimitKernels"/> on the native dialects (#544). A dynamic declaration is placed in the
/// <c>sharedMemBytes</c> of the launch with the CUDA rules (each region aligned to 16 bytes); a launch that does not give
/// it enough throws <see cref="ArgumentOutOfRangeException"/> on the CPU, where CUDA traps. Static declarations are not
/// counted, and the CPU has no limit of its own on bytes or declarations.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SharedMemoryLimitTests : TranspilerTestBase
{
    private const string LimitMessage = "G#: dynamic shared memory exceeds sharedMemBytes";

    // Issue (a): 32 ints need 128 bytes
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_DynamicDeclaration_LargerThanSharedMemBytes_Throws(bool withBarrier)
    {
        SkipUndefinedOnCuda();
        var output = new int[32];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Launch(withBarrier, output, 32, sharedMemBytes: 16));

        Assert.Contains(LimitMessage, exception.Message);
        Assert.Contains("SharedMemoryLimitKernels.cu.cs(", exception.Message);
    }

    // A negative count arrives as at least 2^63 bytes and int.MaxValue ints are 8 GB: both exceed sharedMemBytes before any
    // memory is handed out. The step ends there, and the process goes on: the next launch runs
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MaxValue, true)]
    public void Test_DynamicDeclaration_NegativeOrHugeCount_ThrowsAndProcessGoesOn(int count, bool withBarrier)
    {
        SkipUndefinedOnCuda();
        var output = new int[32];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Launch(withBarrier, output, count, sharedMemBytes: 128));

        Assert.Contains(LimitMessage, exception.Message);
        Launch(withBarrier, output, 32, sharedMemBytes: 128);
        Assert.Equal(Enumerable.Range(0, 32).Select(i => withBarrier ? (i + 1) % 32 + 1 : 2 * i), output);
    }

    // Issue (b): 80,000 bytes, past the old 64 KB limit of the native path (which ended the process). CUDA needs the
    // opt-in of more than 48 KB (#545), so this runs on the CPU only
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_DynamicDeclaration_LargerThan64KB_Runs(bool withBarrier)
    {
        Skip.If(IsCudaDialect, "More than 48 KB of shared memory needs the opt-in of #545 on CUDA.");
        const int Count = 20000;
        var output = new int[Count];

        Launch(withBarrier, output, Count, sharedMemBytes: Count * sizeof(int), blockDimX: 256);

        Assert.Equal(Enumerable.Range(0, Count).Select(i => withBarrier ? (i + 1) % Count + 1 : 2 * i), output);
    }

    // 5 ints end at 20; the doubles start at 32 (16-byte alignment) and end at 56
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TwoDynamicRegions_ExactSize_Runs()
    {
        var output = new int[8];

        SharedMemoryLimitKernels.LaunchTwoRegionsKernel(output, 5, 3, blockDimX: 8, sharedMemBytes: 56);

        Assert.Equal([1 + 1, 2 + 3, 3 + 5, 4, 5, 0, 0, 0], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TwoDynamicRegions_OneByteShort_Throws()
    {
        SkipUndefinedOnCuda();
        var output = new int[8];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SharedMemoryLimitKernels.LaunchTwoRegionsKernel(output, 5, 3, blockDimX: 8, sharedMemBytes: 55));

        Assert.Contains(LimitMessage, exception.Message);
    }

    // 70 static declarations are more than the old 64 of the native path (which ended the process), and are not counted
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ManyStaticDeclarations_NotCountedAndNotLimited()
    {
        const int Threads = 32;
        var output = new int[Threads];

        SharedMemoryLimitKernels.LaunchManyStaticDeclarationsKernel(output, Threads, blockDimX: Threads,
            sharedMemBytes: Threads * sizeof(int));

        // Each static buffer k holds k: 0 + 1 + ... + 69
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t + 69 * 70 / 2), output);
    }

    private void Launch(bool withBarrier, int[] output, int count, uint sharedMemBytes, uint blockDimX = 32)
    {
        if (withBarrier)
        {
            SharedMemoryLimitKernels.LaunchFillKernel(output, count, blockDimX: blockDimX, sharedMemBytes: sharedMemBytes);
        }
        else
        {
            SharedMemoryLimitKernels.LaunchFillWithoutBarrierKernel(output, count, blockDimX: blockDimX,
                sharedMemBytes: sharedMemBytes);
        }
    }

    private void SkipUndefinedOnCuda() =>
        Skip.If(IsCudaDialect, "CUDA traps on these launches; only the CPU paths turn them into exceptions.");
}
