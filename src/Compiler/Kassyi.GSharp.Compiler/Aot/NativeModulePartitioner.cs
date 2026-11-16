using System.Collections.Immutable;
using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Partitions the kernels of a native module into translation units that compile in parallel: a kernel goes to the
/// unit that the hash of its class name picks, so that editing one class changes one unit. Plain data: no I/O and no
/// <see cref="SourceWriter"/>.
/// </summary>
internal static class NativeModulePartitioner
{
    /// <summary>One mebibyte in bytes (1,048,576 bytes).</summary>
    public const long OneMib = 1024L * 1024L;

    /// <summary>The maximum number of translation units.</summary>
    public const int MaxUnits = 32;

    /// <summary>
    /// The number of translation units B for <paramref name="totalBytes"/> of kernel source:
    /// <c>clamp(2^ceil(log2(totalBytes / 1 MiB)), 1, 32)</c>, so that a unit holds at most 1 MiB until the maximum.
    /// </summary>
    public static int ComputeUnitCount(long totalBytes)
    {
        var unitCount = 1;
        while (unitCount < MaxUnits && unitCount * OneMib < totalBytes)
        {
            unitCount *= 2;
        }

        return unitCount;
    }

    /// <summary>The 64-bit FNV-1a hash of <paramref name="classFullName"/>, fed one UTF-16 code unit at a time.</summary>
    public static ulong ComputeClassHash(string classFullName) => Fnv1A64.Compute(classFullName);

    /// <summary>The unit of a class: <c>Fnv1A64(classFullName) mod B</c>.</summary>
    public static int ComputeUnitIndex(string classFullName, int unitCount) =>
        unitCount <= 1 ? 0 : (int)(ComputeClassHash(classFullName) % (ulong)unitCount);

    /// <summary>Plans the partition of <paramref name="kernels"/> into translation units.</summary>
    public static NativeModulePlan Plan(ReadOnlySpan<KernelPartitionItem> kernels)
    {
        long totalBytes = 0;
        foreach (var kernel in kernels)
        {
            totalBytes += kernel.ByteCount;
        }

        var unitCount = ComputeUnitCount(totalBytes);
        var indices = ImmutableArray.CreateBuilder<int>(kernels.Length);
        var unitBytes = new long[unitCount];
        foreach (var kernel in kernels)
        {
            var unitIndex = ComputeUnitIndex(kernel.ClassFullName, unitCount);
            indices.Add(unitIndex);
            unitBytes[unitIndex] += kernel.ByteCount;
        }

        return new NativeModulePlan(unitCount, indices.MoveToImmutable(), ImmutableArray.Create(unitBytes));
    }
}
