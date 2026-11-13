#nullable enable
using Kassyi.GSharp.Compiler.Targets;

namespace Kassyi.GSharp.Tests.Shared.Targets;

/// <summary>
/// The launch limits of the GPUs a <c>GSharpTargetArchitectures</c> setting may run on: which compute capabilities each
/// code reaches, and the strictest value of each limit over them.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuArchitectureLimitsTests
{
    private const int KiB = 1024;

    // Every row from 8.6 on: the default PTX compute_86 runs on all of them
    private static readonly string[] s_fromSm86 =
        ["8.6", "8.7", "8.9", "9.0", "10.0", "10.3", "10.7", "11.0", "12.0", "12.1"];

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("compute_86,sm_86;compute_86,compute_86")]
    // compute_86 PTX alone reaches the same GPUs, and sm_86 alone is SASS plus that PTX
    [InlineData("compute_86")]
    [InlineData("sm_86")]
    public void Resolve_DefaultArchitectures_TakesTheStrictestOfEveryGpuFrom86On(string? setting)
    {
        var limits = GpuArchitectureLimits.Resolve(setting);

        Assert.Equal(s_fromSm86, Versions(limits));
        Assert.True(limits.IsKnown);
        Assert.Equal(99 * KiB, limits.MaxSharedMemoryPerBlockOptIn);
        Assert.Equal(16, limits.MaxBlocksPerMultiprocessor);
        // 10.7 holds 1024 threads per multiprocessor, the fewest from 8.6 on
        Assert.Equal(1024, limits.MaxThreadsPerMultiprocessor);
        Assert.Equal(new[] { new SharedMemoryBudget(100 * KiB, KiB, 128) }, limits.SharedMemoryBudgets);
        Assert.Empty(limits.UnknownArchitectures);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // SASS runs on the newer minor versions of its major version, not on the Tegra 8.7
    [InlineData("compute_86,sm_86", new[] { "8.6", "8.9" }, 99 * KiB, 16, 1536)]
    [InlineData("compute_80,sm_80", new[] { "8.0", "8.6", "8.9" }, 99 * KiB, 16, 1536)]
    [InlineData("compute_90,sm_90", new[] { "9.0" }, 227 * KiB, 32, 2048)]
    [InlineData("compute_75,sm_75", new[] { "7.5" }, 64 * KiB, 16, 1024)]
    // The Tegra 8.7 SASS runs on 8.7 only
    [InlineData("compute_87,sm_87", new[] { "8.7" }, 163 * KiB, 16, 1536)]
    // Architecture-specific code runs on its own compute capability only, family-specific code on its family
    [InlineData("sm_90a", new[] { "9.0" }, 227 * KiB, 32, 2048)]
    [InlineData("sm_100a", new[] { "10.0" }, 227 * KiB, 32, 2048)]
    [InlineData("compute_100a,sm_100a", new[] { "10.0" }, 227 * KiB, 32, 2048)]
    [InlineData("sm_100f", new[] { "10.0", "10.3", "10.7" }, 227 * KiB, 16, 1024)]
    [InlineData("compute_103f,compute_103f", new[] { "10.3", "10.7" }, 227 * KiB, 16, 1024)]
    [InlineData("sm_120f", new[] { "12.0", "12.1" }, 99 * KiB, 24, 1536)]
    // PTX runs on every newer compute capability
    [InlineData(
        "compute_90,compute_90",
        new[] { "9.0", "10.0", "10.3", "10.7", "11.0", "12.0", "12.1" },
        99 * KiB,
        16,
        1024
    )]
    // Several entries: the union of the GPUs, the strictest value of each limit
    [InlineData("compute_90,sm_90;compute_107a,sm_107a", new[] { "9.0", "10.7" }, 227 * KiB, 16, 1024)]
    [InlineData("compute_80,sm_80;compute_90,sm_90", new[] { "8.0", "8.6", "8.9", "9.0" }, 99 * KiB, 16, 1536)]
    public void Resolve_Architectures_ReachTheGpusThatRunThem(
        string setting,
        string[] versions,
        int optIn,
        int blocksPerMultiprocessor,
        int threadsPerMultiprocessor)
    {
        var limits = GpuArchitectureLimits.Resolve(setting);

        Assert.Equal(versions, Versions(limits));
        Assert.Equal(optIn, limits.MaxSharedMemoryPerBlockOptIn);
        Assert.Equal(blocksPerMultiprocessor, limits.MaxBlocksPerMultiprocessor);
        Assert.Equal(threadsPerMultiprocessor, limits.MaxThreadsPerMultiprocessor);
        Assert.Empty(limits.UnknownArchitectures);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Resolve_Sm75Only_KeepsTheTuringBudgetWithItsGranularity()
    {
        var limits = GpuArchitectureLimits.Resolve("compute_75,sm_75");

        Assert.Equal(new[] { new SharedMemoryBudget(64 * KiB, 0, 256) }, limits.SharedMemoryBudgets);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Resolve_TuringAndAmpere_KeepsBothBudgetsSinceNeitherAlwaysHoldsFewer()
    {
        // sm_75 alone is SASS for 7.5 plus PTX that every newer GPU runs: 7.5 has less shared memory, but 8.x reserves
        // 1 KB per block, so for some block sizes either one holds fewer blocks
        var limits = GpuArchitectureLimits.Resolve("sm_75");

        Assert.Equal(64 * KiB, limits.MaxSharedMemoryPerBlockOptIn);
        Assert.Equal(
            new[] { new SharedMemoryBudget(64 * KiB, 0, 256), new SharedMemoryBudget(100 * KiB, KiB, 128) },
            limits.SharedMemoryBudgets
        );
        // A block of 1 byte: 7.5 allocates 256 bytes (256 blocks), 8.6 allocates 1 KB + 128 bytes (88 blocks)
        Assert.Equal(88, limits.ResidentBlocksBySharedMemory(1));
        // A block of 40 KB: 7.5 fits one, 8.6 fits two
        Assert.Equal(1, limits.ResidentBlocksBySharedMemory(40 * KiB));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(null)]
    [InlineData("sm_75")]
    [InlineData("compute_75,sm_75;compute_86,sm_86")]
    [InlineData("compute_80,sm_80")]
    [InlineData("sm_100f;compute_75,sm_75")]
    public void ResidentBlocksBySharedMemory_AnyBlockSize_EqualsTheFewestOverEveryTargetGpu(string? setting)
    {
        var limits = GpuArchitectureLimits.Resolve(setting);
        long[] sizes = [0, 1, 127, 128, 129, 255, 256, 257, 1023, 1024, 1025, 40 * KiB, 48 * KiB, 63 * KiB, 64 * KiB,
            99 * KiB, 99 * KiB + 1, 163 * KiB, 227 * KiB, 327 * KiB];

        foreach (var size in sizes)
        {
            var expected = limits.Architectures.Min(row => row.SharedMemoryBudget.ResidentBlocks(size));
            Assert.True(expected == limits.ResidentBlocksBySharedMemory(size), $"bytes per block {size}");
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    // 8.6: 100 KB per multiprocessor, 1 KB reserved per block, 128-byte units
    [InlineData(102400, 1024, 128, 0, 100)]
    [InlineData(102400, 1024, 128, 101376, 1)]
    [InlineData(102400, 1024, 128, 101377, 0)]
    [InlineData(102400, 1024, 128, 50 * 1024 - 1024, 2)]
    [InlineData(102400, 1024, 128, 50 * 1024 - 1023, 1)]
    // 7.5: nothing reserved, 256-byte units; a block without shared memory takes none
    [InlineData(65536, 0, 256, 0, int.MaxValue)]
    [InlineData(65536, 0, 256, 1, 256)]
    [InlineData(65536, 0, 256, 256, 256)]
    [InlineData(65536, 0, 256, 257, 128)]
    public void ResidentBlocks_BlockSize_RoundsTheBlockAndItsReservationUpToTheUnit(
        int perMultiprocessor,
        int reserved,
        int granularity,
        long bytesPerBlock,
        int expected)
    {
        var budget = new SharedMemoryBudget(perMultiprocessor, reserved, granularity);

        Assert.Equal(expected, budget.ResidentBlocks(bytesPerBlock));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    // A compute capability newer than the table reaches no GPU: only the limits every GPU shares are left
    [InlineData("compute_130,sm_130", new[] { "sm_130" }, new string[0])]
    [InlineData("sm_123456", new[] { "sm_123456", "compute_123456" }, new string[0])]
    // An older one still reaches the GPUs of the table that run it, and is named once
    [InlineData(
        "sm_72;compute_72,sm_72",
        new[] { "sm_72", "compute_72" },
        new[] { "7.5", "8.0", "8.6", "8.7", "8.9", "9.0", "10.0", "10.3", "10.7", "11.0", "12.0", "12.1" }
    )]
    [InlineData("compute_86,sm_86;compute_101,sm_101", new[] { "sm_101" }, new[] { "8.6", "8.9", "10.3", "10.7" })]
    public void Resolve_ComputeCapabilityNotInTheTable_IsNamedAndLeftOut(
        string setting,
        string[] unknown,
        string[] versions)
    {
        var limits = GpuArchitectureLimits.Resolve(setting);

        Assert.Equal(unknown, limits.UnknownArchitectures);
        Assert.Equal(versions, Versions(limits));
        Assert.Equal(versions.Length != 0, limits.IsKnown);
        if (!limits.IsKnown)
        {
            Assert.Equal(0, limits.MaxSharedMemoryPerBlockOptIn);
            Assert.Equal(0, limits.MaxBlocksPerMultiprocessor);
            Assert.Equal(0, limits.MaxThreadsPerMultiprocessor);
            Assert.Empty(limits.SharedMemoryBudgets);
            Assert.Equal(int.MaxValue, limits.ResidentBlocksBySharedMemory(1));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Resolve_MalformedEntry_IsSkipped()
    {
        // The build reports the malformed entry as GS0004; the limits come from the valid ones
        var limits = GpuArchitectureLimits.Resolve("foo;compute_90,sm_90;sm_8");

        Assert.Equal(new[] { "9.0" }, Versions(limits));
        Assert.Empty(limits.UnknownArchitectures);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_SameSetting_ComparesEqual()
    {
        // The generator steps after the setting stay cached while it does not change
        Assert.Equal(GpuArchitectureLimits.Resolve("sm_75;sm_120f"), GpuArchitectureLimits.Resolve("sm_75;sm_120f"));
        Assert.NotEqual(GpuArchitectureLimits.Resolve("sm_75"), GpuArchitectureLimits.Resolve("compute_75,sm_75"));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // compute capability, resident blocks and threads per multiprocessor, shared memory per multiprocessor and per
    // block with opt-in (KB): the CUDA Programming Guide, "Compute Capabilities" (Tables 33 and 34). Resident blocks,
    // the largest shared memory configuration and the allocation unit agree with cuda_occupancy.h of CUDA 13.4
    // (cudaOccMaxBlocksPerMultiprocessor, cudaOccAlignUpShmemSizeVoltaPlus, cudaOccSMemAllocationGranularity)
    [InlineData(7, 5, 16, 1024, 64, 64, 256)]
    [InlineData(8, 0, 32, 2048, 164, 163, 128)]
    [InlineData(8, 6, 16, 1536, 100, 99, 128)]
    [InlineData(8, 7, 16, 1536, 164, 163, 128)]
    [InlineData(8, 9, 24, 1536, 100, 99, 128)]
    [InlineData(9, 0, 32, 2048, 228, 227, 128)]
    [InlineData(10, 0, 32, 2048, 228, 227, 128)]
    [InlineData(10, 3, 32, 2048, 228, 227, 128)]
    [InlineData(10, 7, 16, 1024, 328, 327, 128)]
    [InlineData(11, 0, 24, 1536, 228, 227, 128)]
    [InlineData(12, 0, 24, 1536, 100, 99, 128)]
    [InlineData(12, 1, 24, 1536, 100, 99, 128)]
    public void Table_ComputeCapability_MatchesThePublishedLimits(
        int major,
        int minor,
        int blocks,
        int threads,
        int sharedKiB,
        int optInKiB,
        int granularity)
    {
        var row = Assert.Single(GpuArchitectureLimits.Table, r => r.Major == major && r.Minor == minor);

        Assert.Equal(blocks, row.MaxBlocksPerMultiprocessor);
        Assert.Equal(threads, row.MaxThreadsPerMultiprocessor);
        Assert.Equal(sharedKiB * KiB, row.SharedMemoryPerMultiprocessor);
        Assert.Equal(optInKiB * KiB, row.MaxSharedMemoryPerBlockOptIn);
        Assert.Equal(granularity, row.SharedMemoryAllocationGranularity);
        Assert.Equal(major == 8 && minor == 7, row.IsTegra);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Table_EveryRow_FitsOneOptInBlockOfAnyThreadCount()
    {
        Assert.Equal(12, GpuArchitectureLimits.Table.Length);
        Assert.Equal(
            GpuArchitectureLimits.Table.OrderBy(r => r.Major).ThenBy(r => r.Minor),
            GpuArchitectureLimits.Table
        );
        foreach (var row in GpuArchitectureLimits.Table)
        {
            // The driver reserves what the opt-in limit leaves of a multiprocessor (1 KB from 8.0 on), so a block at
            // the opt-in limit still fits once, and 1024 threads (the most per block) fit once
            Assert.Equal(row.SharedMemoryPerMultiprocessor - row.MaxSharedMemoryPerBlockOptIn,
                row.ReservedSharedMemoryPerBlock);
            Assert.Equal(row.Major >= 8 ? KiB : 0, row.ReservedSharedMemoryPerBlock);
            Assert.Equal(1, row.SharedMemoryBudget.ResidentBlocks(row.MaxSharedMemoryPerBlockOptIn));
            Assert.True(row.MaxThreadsPerMultiprocessor >= 1024, row.Name);
        }
    }

    private static string[] Versions(TargetLimits limits) =>
        limits.Architectures.Select(row => $"{row.Major}.{row.Minor}").ToArray();
}
