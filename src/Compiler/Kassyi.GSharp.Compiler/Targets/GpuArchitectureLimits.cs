using Kassyi.GSharp.Cuda;

namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The shared memory limit per block of each compute capability, and the limit of the GPUs a
/// <c>GSharpTargetArchitectures</c> setting may run on.
/// </summary>
/// <remarks>
/// The values are those of the CUDA Programming Guide ("Compute Capabilities", the maximum amount of shared memory per
/// thread block with opt-in). The 12.x column of the guide covers 12.0 and 12.1, the members of the compute_120f
/// family.
/// </remarks>
internal static class GpuArchitectureLimits
{
    private const int KiB = 1024;

    /// <summary>The compute capabilities whose limits G# knows, oldest first.</summary>
    public static ImmutableArray<GpuArchitecture> Table { get; } = ImmutableArray.Create<GpuArchitecture>(
        new(7, 5, 64 * KiB),
        new(8, 0, 163 * KiB),
        new(8, 6, 99 * KiB),
        new(8, 7, 163 * KiB, IsTegra: true),
        new(8, 9, 99 * KiB),
        new(9, 0, 227 * KiB),
        new(10, 0, 227 * KiB),
        new(10, 3, 227 * KiB),
        new(10, 7, 327 * KiB),
        new(11, 0, 227 * KiB),
        new(12, 0, 99 * KiB),
        new(12, 1, 99 * KiB)
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
                unknown.AsEquatableArray()
            );
        }

        return new TargetLimits(
            rows.AsEquatableArray(),
            rows
                .AsStackEnumerable()
                .Select(static row => row.MaxSharedMemoryPerBlockOptIn)
                .Min(),
            unknown.AsEquatableArray()
        );
    }
}
