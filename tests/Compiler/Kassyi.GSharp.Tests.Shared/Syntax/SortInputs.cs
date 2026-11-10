using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Deterministic sort inputs (fixed seeds).</summary>
internal static class SortInputs
{
    public static int[] Int(int length, SortPattern pattern, int seed)
    {
        var random = new Random(seed);
        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pattern switch
            {
                SortPattern.Random => random.Next(int.MinValue, int.MaxValue),
                SortPattern.FewDistinct => random.Next(0, 4),
                SortPattern.Ascending => i,
                SortPattern.Descending => length - i,
                _ => 7,
            };
        }

        return values;
    }

    // Many equal keys with different bits: -0.0 / 0.0 and NaNs with different payloads,
    // mixed with a few ordinary values
    public static float[] Single(int length, int seed)
    {
        float[] pool =
        [
            -0f, 0f, 1f, -1f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
            BitConverter.Int32BitsToSingle(0x7fc00001), BitConverter.Int32BitsToSingle(unchecked((int)0xffc00002)),
        ];
        var random = new Random(seed);
        var values = new float[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pool[random.Next(pool.Length)];
        }

        return values;
    }

    public static double[] Double(int length, int seed)
    {
        double[] pool =
        [
            -0d, 0d, 1d, -1d, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            BitConverter.Int64BitsToDouble(0x7ff8000000000001),
            BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000002)),
        ];
        var random = new Random(seed);
        var values = new double[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = pool[random.Next(pool.Length)];
        }

        return values;
    }

    /// <summary>
    /// McIlroy's adversary ("A Killer Adversary for Quicksort", 1999) run against the .NET introsort: the values are
    /// fixed lazily, so that each partition peels off only a few elements and the depth limit runs out
    /// (the heap sort path).
    /// </summary>
    public static int[] HeapSortKiller(int length)
    {
        var gas = length;
        var values = new int[length];
        Array.Fill(values, gas);
        var order = new int[length];
        for (var i = 0; i < length; i++)
        {
            order[i] = i;
        }

        var solid = 0;
        var candidate = 0;
        MemoryExtensions.Sort(order.AsSpan(), (x, y) =>
        {
            if (values[x] == gas && values[y] == gas)
            {
                values[x == candidate ? x : y] = solid++;
            }

            if (values[x] == gas)
            {
                candidate = x;
            }
            else if (values[y] == gas)
            {
                candidate = y;
            }

            return values[x].CompareTo(values[y]);
        });

        // The values never compared with each other while gas are larger than every frozen one, in any order
        for (var i = 0; i < length; i++)
        {
            if (values[i] == gas)
            {
                values[i] = solid++;
            }
        }

        return values;
    }
}
