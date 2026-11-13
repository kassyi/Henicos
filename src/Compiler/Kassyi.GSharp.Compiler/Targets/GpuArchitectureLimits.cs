using Kassyi.GSharp.Cuda;

namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The launch limits per compute capability, and the limits of the GPUs a <c>GSharpTargetArchitectures</c> setting
/// may run on.
/// </summary>
/// <remarks>
/// The values are those of the CUDA Programming Guide ("Compute Capabilities": technical specifications and shared
/// memory capacity). The reserved bytes per block are the shared memory per multiprocessor less the opt-in limit per
/// block, which the driver reports as <c>CU_DEVICE_ATTRIBUTE_RESERVED_SHARED_MEMORY_PER_BLOCK</c> (1 KB from 8.0 on);
/// the allocation unit is 256 bytes before 8.0 and 128 bytes from 8.0 on. The 12.x column of the guide covers 12.0
/// and 12.1, the members of the compute_120f family.
/// </remarks>
internal static class GpuArchitectureLimits
{
    private const int KiB = 1024;

    /// <summary>The compute capabilities whose limits G# knows, oldest first.</summary>
    public static ImmutableArray<GpuArchitecture> Table { get; } = ImmutableArray.Create<GpuArchitecture>(
        new(7, 5, 16, 1024, 64 * KiB, 64 * KiB, 0, 256),
        new(8, 0, 32, 2048, 164 * KiB, 163 * KiB, KiB, 128),
        new(8, 6, 16, 1536, 100 * KiB, 99 * KiB, KiB, 128),
        new(8, 7, 16, 1536, 164 * KiB, 163 * KiB, KiB, 128, IsTegra: true),
        new(8, 9, 24, 1536, 100 * KiB, 99 * KiB, KiB, 128),
        new(9, 0, 32, 2048, 228 * KiB, 227 * KiB, KiB, 128),
        new(10, 0, 32, 2048, 228 * KiB, 227 * KiB, KiB, 128),
        new(10, 3, 32, 2048, 228 * KiB, 227 * KiB, KiB, 128),
        new(10, 7, 16, 1024, 328 * KiB, 327 * KiB, KiB, 128),
        new(11, 0, 24, 1536, 228 * KiB, 227 * KiB, KiB, 128),
        new(12, 0, 24, 1536, 100 * KiB, 99 * KiB, KiB, 128),
        new(12, 1, 24, 1536, 100 * KiB, 99 * KiB, KiB, 128)
    );

    /// <summary>
    /// The limits of every GPU the fatbin built from <paramref name="targetArchitectures"/> may run on; an empty
    /// setting stands for <see cref="CudaArchitectureList.DefaultTargetArchitectures"/>. Invalid entries are skipped
    /// (the build reports them as GS0004).
    /// </summary>
    /// <remarks>Which GPUs run each <c>-gencode</c> code: <see cref="GencodeTarget.Runs"/>.</remarks>
    public static TargetLimits Resolve(string? targetArchitectures)
    {
        var text = targetArchitectures is { } setting && !string.IsNullOrWhiteSpace(setting)
            ? setting
            : CudaArchitectureList.DefaultTargetArchitectures;
        var reached = new HashSet<GpuArchitecture>();
        var unknown = ImmutableArray.CreateBuilder<string>();
        var rest = text.AsSpan();
        while (CudaArchitectureList.TryReadEntry(ref rest, out var entry))
        {
            var count = CudaArchitectureList.ReadGencodes(entry, out var first, out var second);
            if (count >= 1)
            {
                Reach(first.Code, reached, unknown);
            }

            if (count == 2)
            {
                Reach(second.Code, reached, unknown);
            }
        }

        return Combine(reached, unknown.ToImmutable());
    }

    // Adds the rows that run the code; records the code when its own compute capability is not a row
    private static void Reach(string code, HashSet<GpuArchitecture> reached, ImmutableArray<string>.Builder unknown)
    {
        var target = GencodeTarget.Read(code);
        if (target is not { } known || !IsInTable(known.Major, known.Minor))
        {
            if (!unknown.Contains(code))
            {
                unknown.Add(code);
            }
        }

        if (target is { } runnable)
        {
            Table
                .AsStackEnumerable()
                .Where(runnable, static (row, gencode) => gencode.Runs(row))
                .AddTo(reached);
        }
    }

    private static bool IsInTable(int major, int minor) => Table
        .AsStackEnumerable()
        .Any((major, minor), static (row, version) => row.Major == version.major && row.Minor == version.minor);

    private static TargetLimits Combine(HashSet<GpuArchitecture> reached, ImmutableArray<string> unknown)
    {
        var rows = ImmutableArray.Create(Table
            .AsStackEnumerable()
            .Where(reached, static (row, set) => set.Contains(row))
            .ToArray()
        );
        if (rows.IsEmpty)
        {
            return new TargetLimits(
                ImmutableArray<GpuArchitecture>.Empty.AsEquatableArray(),
                0,
                0,
                0,
                ImmutableArray<SharedMemoryBudget>.Empty.AsEquatableArray(),
                unknown.AsEquatableArray()
            );
        }

        return new TargetLimits(
            rows.AsEquatableArray(),
            rows
                .AsStackEnumerable()
                .Select(static row => row.MaxSharedMemoryPerBlockOptIn)
                .Min(),
            rows
                .AsStackEnumerable()
                .Select(static row => row.MaxBlocksPerMultiprocessor)
                .Min(),
            rows
                .AsStackEnumerable()
                .Select(static row => row.MaxThreadsPerMultiprocessor)
                .Min(),
            GetFewestBlockBudgets(rows).AsEquatableArray(),
            unknown.AsEquatableArray()
        );
    }

    // The distinct budgets of the rows, without those that another budget never exceeds
    private static ImmutableArray<SharedMemoryBudget> GetFewestBlockBudgets(ImmutableArray<GpuArchitecture> rows)
    {
        var distinct = ImmutableArray.CreateBuilder<SharedMemoryBudget>();
        rows
            .AsStackEnumerable()
            .Select(static row => row.SharedMemoryBudget)
            .AddIf(distinct, distinct, static (budget, seen) => !seen.Contains(budget));
        var all = distinct.ToImmutable();
        return ImmutableArray.Create(all
            .AsStackEnumerable()
            .Where(all, static (budget, others) => !others
                .AsStackEnumerable()
                .Any(budget, static (other, self) => other != self && other.IsAtMost(self))
            ).ToArray()
        );
    }
}
