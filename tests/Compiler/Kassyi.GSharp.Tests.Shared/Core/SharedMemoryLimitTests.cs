#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemoryLimitKernels"/> on CUDA and on both CPU paths, native and managed (#544). A dynamic
/// declaration is placed in the <c>sharedMemBytes</c> of the launch with the CUDA rules (each region aligned to 16
/// bytes); a launch that does not give it enough throws <see cref="ArgumentOutOfRangeException"/> on the CPU paths, where
/// CUDA traps. Static declarations are not counted, and the CPU paths have no limit of their own on bytes or declarations.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedMemoryLimitTests : TranspilerTestBase
{
    private const string LimitMessage = "G#: dynamic shared memory exceeds sharedMemBytes";

    // Issue (a): 32 ints need 128 bytes
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Test_DynamicDeclaration_LargerThanSharedMemBytes_Throws(bool forceManaged, bool withBarrier)
    {
        SkipUndefinedOnCuda();
        var output = new int[32];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run(forceManaged, () => Launch(withBarrier, output, 32, sharedMemBytes: 16)));

        Assert.Contains(LimitMessage, exception.Message);
        Assert.Contains("SharedMemoryLimitKernels.cu.cs(", exception.Message);
    }

    // A negative count arrives as at least 2^63 bytes and int.MaxValue ints are 8 GB: both exceed sharedMemBytes before any
    // memory is handed out. The step ends there, and the process goes on: the next launch runs
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, -1, false)]
    [InlineData(false, -1, true)]
    [InlineData(false, int.MaxValue, false)]
    [InlineData(false, int.MaxValue, true)]
    [InlineData(true, -1, false)]
    [InlineData(true, -1, true)]
    [InlineData(true, int.MaxValue, false)]
    [InlineData(true, int.MaxValue, true)]
    public void Test_DynamicDeclaration_NegativeOrHugeCount_ThrowsAndProcessGoesOn(bool forceManaged, int count, bool withBarrier)
    {
        SkipUndefinedOnCuda();
        var output = new int[32];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run(forceManaged, () => Launch(withBarrier, output, count, sharedMemBytes: 128)));

        Assert.Contains(LimitMessage, exception.Message);
        Run(forceManaged, () => Launch(withBarrier, output, 32, sharedMemBytes: 128));
        Assert.Equal(Enumerable.Range(0, 32).Select(i => withBarrier ? (i + 1) % 32 + 1 : 2 * i), output);
    }

    // Issue (b): 80,000 bytes, past the old 64 KB limit of the native path (which ended the process). CUDA needs the
    // opt-in of more than 48 KB (#545), so this runs on the CPU only
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Test_DynamicDeclaration_LargerThan64KB_Runs(bool forceManaged, bool withBarrier)
    {
        Skip.If(IsCudaDialect, "More than 48 KB of shared memory needs the opt-in of #545 on CUDA.");
        const int Count = 20000;
        var output = new int[Count];

        Run(forceManaged, () => Launch(withBarrier, output, Count, sharedMemBytes: Count * sizeof(int), blockDimX: 256));

        Assert.Equal(Enumerable.Range(0, Count).Select(i => withBarrier ? (i + 1) % Count + 1 : 2 * i), output);
    }

    // 5 ints end at 20; the doubles start at 32 (16-byte alignment) and end at 56
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_TwoDynamicRegions_ExactSize_Runs(bool forceManaged)
    {
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLimitKernels.LaunchTwoRegionsKernel(output, 5, 3, blockDimX: 8, sharedMemBytes: 56));

        Assert.Equal([1 + 1, 2 + 3, 3 + 5, 4, 5, 0, 0, 0], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_TwoDynamicRegions_OneByteShort_Throws(bool forceManaged)
    {
        SkipUndefinedOnCuda();
        var output = new int[8];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run(forceManaged, () => SharedMemoryLimitKernels.LaunchTwoRegionsKernel(output, 5, 3, blockDimX: 8, sharedMemBytes: 55)));

        Assert.Contains(LimitMessage, exception.Message);
    }

    // 70 static declarations are more than the old 64 of the native path (which ended the process), and are not counted
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ManyStaticDeclarations_NotCountedAndNotLimited(bool forceManaged)
    {
        const int Threads = 32;
        var output = new int[Threads];

        Run(forceManaged, () => SharedMemoryLimitKernels.LaunchManyStaticDeclarationsKernel(output, Threads, blockDimX: Threads,
            sharedMemBytes: Threads * sizeof(int)));

        // Each static buffer k holds k: 0 + 1 + ... + 69
        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => t + 69 * 70 / 2), output);
    }

    // Two blocks with counts 4 and 8, kept across a barrier (managed: the redeclaration at a resume finds the region only)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_CountKeptAcrossBarrier_Runs(bool forceManaged)
    {
        var output = new int[16];

        Run(forceManaged, () => SharedMemoryLimitKernels.LaunchHoistedCountKernel(output, gridDimX: 2, blockDimX: 8,
            sharedMemBytes: 8 * sizeof(int)));

        int[] expected = [2, 3, 4, 1, 0, 0, 0, 0, 2, 3, 4, 5, 6, 7, 8, 1];
        Assert.Equal(expected, output);
    }

    // The device function's region is placed before the kernel's, as the kernel reaches them: 56 bytes run, 55 throw on
    // the CPU paths (the reverse order would need only 52). A path that declared the kernel's region at the start of the
    // kernel would pass with 55
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 56u)]
    [InlineData(true, 56u)]
    [InlineData(false, 55u)]
    [InlineData(true, 55u)]
    public void Test_RegionAfterDeviceFunctionRegion_PlacedInTheOrderReached(bool forceManaged, uint sharedMemBytes)
    {
        var output = new int[8];
        void Launch() => SharedMemoryLimitKernels.LaunchRegionOrderKernel(output, 5, 3, blockDimX: 8,
            sharedMemBytes: sharedMemBytes);

        if (sharedMemBytes == 56)
        {
            Run(forceManaged, Launch);
            Assert.Equal([1 + 1, 2 + 3, 3 + 5, 4, 5, 0, 0, 0], output);
            return;
        }

        SkipUndefinedOnCuda();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Run(forceManaged, Launch));
        Assert.Contains(LimitMessage, exception.Message);
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

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    private void SkipUndefinedOnCuda() =>
        Skip.If(IsCudaDialect, "CUDA traps on these launches; only the CPU paths turn them into exceptions.");
}
