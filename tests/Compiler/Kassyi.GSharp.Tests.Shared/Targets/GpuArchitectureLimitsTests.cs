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
        Assert.Equal("sm_86", limits.SharedMemoryLimitArchitecture);
        Assert.Empty(limits.UnknownArchitectures);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // SASS runs on the newer minor versions of its major version, not on the Tegra 8.7
    [InlineData("compute_86,sm_86", new[] { "8.6", "8.9" }, 99 * KiB, "sm_86")]
    [InlineData("compute_80,sm_80", new[] { "8.0", "8.6", "8.9" }, 99 * KiB, "sm_86")]
    [InlineData("compute_90,sm_90", new[] { "9.0" }, 227 * KiB, "sm_90")]
    [InlineData("compute_75,sm_75", new[] { "7.5" }, 64 * KiB, "sm_75")]
    // sm_75 alone adds PTX that every newer GPU runs; 7.5 stays the strictest
    [InlineData(
        "sm_75",
        new[] { "7.5", "8.0", "8.6", "8.7", "8.9", "9.0", "10.0", "10.3", "10.7", "11.0", "12.0", "12.1" },
        64 * KiB,
        "sm_75"
    )]
    // The Tegra 8.7 SASS runs on 8.7 only
    [InlineData("compute_87,sm_87", new[] { "8.7" }, 163 * KiB, "sm_87")]
    // Architecture-specific code runs on its own compute capability only, family-specific code on its family
    [InlineData("sm_90a", new[] { "9.0" }, 227 * KiB, "sm_90")]
    [InlineData("sm_100a", new[] { "10.0" }, 227 * KiB, "sm_100")]
    [InlineData("compute_100a,sm_100a", new[] { "10.0" }, 227 * KiB, "sm_100")]
    [InlineData("sm_100f", new[] { "10.0", "10.3", "10.7" }, 227 * KiB, "sm_100")]
    [InlineData("compute_103f,compute_103f", new[] { "10.3", "10.7" }, 227 * KiB, "sm_103")]
    [InlineData("sm_120f", new[] { "12.0", "12.1" }, 99 * KiB, "sm_120")]
    // PTX runs on every newer compute capability
    [InlineData(
        "compute_90,compute_90",
        new[] { "9.0", "10.0", "10.3", "10.7", "11.0", "12.0", "12.1" },
        99 * KiB,
        "sm_120"
    )]
    // Several entries: the union of the GPUs, the strictest value of each limit
    [InlineData("compute_90,sm_90;compute_107a,sm_107a", new[] { "9.0", "10.7" }, 227 * KiB, "sm_90")]
    [InlineData("compute_80,sm_80;compute_90,sm_90", new[] { "8.0", "8.6", "8.9", "9.0" }, 99 * KiB, "sm_86")]
    public void Resolve_Architectures_ReachTheGpusThatRunThem(
        string setting,
        string[] versions,
        int optIn,
        string strictest)
    {
        var limits = GpuArchitectureLimits.Resolve(setting);

        Assert.Equal(versions, Versions(limits));
        Assert.Equal(optIn, limits.MaxSharedMemoryPerBlockOptIn);
        Assert.Equal(strictest, limits.SharedMemoryLimitArchitecture);
        Assert.Empty(limits.UnknownArchitectures);
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
            Assert.Equal("", limits.SharedMemoryLimitArchitecture);
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
    // compute capability and shared memory per block with opt-in (KB): the CUDA Programming Guide, "Compute
    // Capabilities" (Table 34, "Maximum amount of shared memory per thread block", columns read from the colspans)
    [InlineData(7, 5, 64)]
    [InlineData(8, 0, 163)]
    [InlineData(8, 6, 99)]
    [InlineData(8, 7, 163)]
    [InlineData(8, 9, 99)]
    [InlineData(9, 0, 227)]
    [InlineData(10, 0, 227)]
    [InlineData(10, 3, 227)]
    [InlineData(10, 7, 327)]
    [InlineData(11, 0, 227)]
    [InlineData(12, 0, 99)]
    [InlineData(12, 1, 99)]
    public void Table_ComputeCapability_MatchesThePublishedLimits(
        int major,
        int minor,
        int optInKiB)
    {
        var row = Assert.Single(GpuArchitectureLimits.Table, r => r.Major == major && r.Minor == minor);

        Assert.Equal(optInKiB * KiB, row.MaxSharedMemoryPerBlockOptIn);
        Assert.Equal(major == 8 && minor == 7, row.IsTegra);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Table_Rows_AreDistinctAndOldestFirst()
    {
        // SharedMemoryLimitArchitecture names the first row with the smallest limit, the oldest one
        Assert.Equal(12, GpuArchitectureLimits.Table.Length);
        Assert.Equal(
            GpuArchitectureLimits.Table.OrderBy(r => r.Major).ThenBy(r => r.Minor),
            GpuArchitectureLimits.Table
        );
        Assert.Equal(
            GpuArchitectureLimits.Table.Length,
            GpuArchitectureLimits.Table.Select(r => (r.Major, r.Minor)).Distinct().Count()
        );
    }

    private static string[] Versions(TargetLimits limits) =>
        limits.Architectures.Select(row => $"{row.Major}.{row.Minor}").ToArray();
}
