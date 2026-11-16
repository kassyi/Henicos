using System.Collections.Immutable;
using System.Text;
using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Partitions kernels into independent translation units for parallel compilation (Issue #2008).
/// Pure data transformation: zero I/O and zero SourceWriter.
/// </summary>
internal static class NativeModulePartitioner
{
    /// <summary>One mebibyte in bytes (1,048,576 bytes).</summary>
    public const long OneMib = 1024L * 1024L;

    /// <summary>The maximum number of translation units.</summary>
    public const int MaxUnits = 32;

    /// <summary>
    /// Computes the number of translation units B:
    /// <c>clamp(2^ceil(log2(totalBytes / 1 MiB)), 1, 32)</c>. If totalBytes &lt;= 1 MiB, B = 1.
    /// </summary>
    public static int ComputeUnitCount(long totalBytes)
    {
        if (totalBytes <= OneMib)
        {
            return 1;
        }

        if (totalBytes <= 2L * OneMib)
        {
            return 2;
        }

        if (totalBytes <= 4L * OneMib)
        {
            return 4;
        }

        if (totalBytes <= 8L * OneMib)
        {
            return 8;
        }

        if (totalBytes <= 16L * OneMib)
        {
            return 16;
        }

        return MaxUnits;
    }

    /// <summary>
    /// Computes the 64-bit FNV-1a hash over the UTF-8 bytes of <paramref name="classFullName"/>.
    /// </summary>
    public static ulong ComputeClassHash(string classFullName)
    {
        if (classFullName is null)
        {
            throw new ArgumentNullException(nameof(classFullName));
        }

        var hash = new Fnv1A64();
        var isAscii = true;
        for (var i = 0; i < classFullName.Length; i++)
        {
            if (classFullName[i] > 127)
            {
                isAscii = false;
                break;
            }
        }

        if (isAscii)
        {
            for (var i = 0; i < classFullName.Length; i++)
            {
                hash.Append((byte)classFullName[i]);
            }

            return hash.Value;
        }

        var bytes = Encoding.UTF8.GetBytes(classFullName);
        for (var i = 0; i < bytes.Length; i++)
        {
            hash.Append(bytes[i]);
        }

        return hash.Value;
    }

    /// <summary>
    /// Computes the unit index for a class: <c>Fnv1A64(classFullName, UTF8) mod B</c>.
    /// </summary>
    public static int ComputeUnitIndex(string classFullName, int unitCount)
    {
        if (unitCount <= 1)
        {
            return 0;
        }

        var hash = ComputeClassHash(classFullName);
        return (int)(hash % (ulong)unitCount);
    }

    /// <summary>
    /// Plans the partition of kernels into translation units.
    /// </summary>
    public static NativeModulePlan Plan(ReadOnlySpan<KernelPartitionItem> kernels)
    {
        if (kernels.IsEmpty)
        {
            return new NativeModulePlan(1, ImmutableArray<int>.Empty, ImmutableArray.Create(0L));
        }

        long totalBytes = 0;
        for (var i = 0; i < kernels.Length; i++)
        {
            totalBytes += kernels[i].ByteCount;
        }

        var unitCount = ComputeUnitCount(totalBytes);
        var indices = ImmutableArray.CreateBuilder<int>(kernels.Length);
        var unitBytes = new long[unitCount];

        for (var i = 0; i < kernels.Length; i++)
        {
            var unitIndex = ComputeUnitIndex(kernels[i].ClassFullName, unitCount);
            indices.Add(unitIndex);
            unitBytes[unitIndex] += kernels[i].ByteCount;
        }

        return new NativeModulePlan(unitCount, indices.MoveToImmutable(), ImmutableArray.Create(unitBytes));
    }

    /// <summary>
    /// Overload taking <see cref="IReadOnlyList{KernelPartitionItem}"/>.
    /// </summary>
    public static NativeModulePlan Plan(IReadOnlyList<KernelPartitionItem> kernels)
    {
        if (kernels is null || kernels.Count == 0)
        {
            return new NativeModulePlan(1, ImmutableArray<int>.Empty, ImmutableArray.Create(0L));
        }

        var array = new KernelPartitionItem[kernels.Count];
        for (var i = 0; i < kernels.Count; i++)
        {
            array[i] = kernels[i];
        }

        return Plan(array.AsSpan());
    }
}
