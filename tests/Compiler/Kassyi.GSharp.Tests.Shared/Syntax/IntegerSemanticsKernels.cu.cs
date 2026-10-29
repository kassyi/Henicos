// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Integer operators whose C# result C++ leaves undefined: signed overflow wraps (C# spec §12.10, unchecked), a shift
/// uses only the low 5 or 6 bits of its count (§12.11), <c>&gt;&gt;&gt;</c> shifts in zeros, and a float converted to an
/// integer saturates with NaN as 0 (.NET 9 and later) (#722, #724). <see cref="IntegerSemanticsScenarios"/> compares the
/// results with the same device functions run as C# on the host.
/// </summary>
public static partial class IntegerSemanticsKernels
{
    /// <summary>The results that one (a, b, s) triple writes in <see cref="IntOperators"/>.</summary>
    public const int IntValuesPerTriple = 38;

    /// <summary>The results that one (a, b, s) triple writes in <see cref="LongOperators"/>.</summary>
    public const int LongValuesPerTriple = 29;

    /// <summary>The results that one input writes in <see cref="ConvertOperators"/>.</summary>
    public const int ConversionsPerInput = 18;

    [GpuDeviceFunction]
    public static void IntOperators(int a, int b, int s, int[] o, int index)
    {
        int v = index * IntValuesPerTriple;
        o[v] = a + b;
        o[v + 1] = a - b;
        o[v + 2] = a * b;
        o[v + 3] = -a;
        o[v + 4] = a << s;
        o[v + 5] = a >> s;
        o[v + 6] = a >>> s;
        o[v + 7] = a << 33;
        o[v + 8] = a >> 40;
        o[v + 9] = a >>> 31;

        // nvcc folds these to 0 when the arithmetic is signed C++ (#722)
        o[v + 10] = a + 1 < a ? 1 : 0;
        o[v + 11] = a - 1 > a ? 1 : 0;
        o[v + 12] = a * 2 / 2 != a ? 1 : 0;

        int x = a;
        x += b;
        o[v + 13] = x;
        x -= s;
        o[v + 14] = x;
        x *= b;
        o[v + 15] = x;
        x <<= s;
        o[v + 16] = x;
        x >>= s;
        o[v + 17] = x;
        x >>>= s;
        o[v + 18] = x;

        int y = a;
        o[v + 19] = y++;
        o[v + 20] = y;
        o[v + 21] = ++y;
        o[v + 22] = y--;
        o[v + 23] = --y;
        y--;
        o[v + 24] = y;

        // The operator is int for the narrow types: 65535 * 65535 overflows int, -1 << 1 is undefined in C++17
        short sh = (short)a;
        sh += (short)b;
        o[v + 25] = sh;
        sbyte sb = (sbyte)a;
        sb <<= s;
        o[v + 26] = sb;
        ushort us = (ushort)b;
        o[v + 27] = us * us;
        o[v + 28] = (sbyte)a << s;

        uint u = (uint)a;
        o[v + 29] = (int)(u << s);
        o[v + 30] = (int)(u >> s);
        o[v + 31] = (int)(u >>> s);
        u <<= s;
        o[v + 32] = (int)u;

        // Mixed with uint, C# computes in long (#671); the long result is narrowed by the cast
        o[v + 33] = (int)((uint)b * a);
        o[v + 34] = -(a * b);
        o[v + 35] = (a + b) * (a - b);

        // A target whose evaluation writes something (k++) is evaluated once
        int k = v + 36;
        o[k++] += a;
        o[k++]--;
    }

    [GpuDeviceFunction]
    public static void LongOperators(long a, long b, int s, long[] o, int index)
    {
        int v = index * LongValuesPerTriple;
        o[v] = a + b;
        o[v + 1] = a - b;
        o[v + 2] = a * b;
        o[v + 3] = -a;
        o[v + 4] = a << s;
        o[v + 5] = a >> s;
        o[v + 6] = a >>> s;
        o[v + 7] = a << 65;
        o[v + 8] = a >> 70;
        o[v + 9] = a + 1 < a ? 1 : 0;

        long x = a;
        x += b;
        o[v + 10] = x;
        x *= b;
        o[v + 11] = x;
        x <<= s;
        o[v + 12] = x;
        x >>>= s;
        o[v + 13] = x;

        long y = a;
        o[v + 14] = y++;
        o[v + 15] = ++y;
        o[v + 16] = y--;
        o[v + 17] = --y;

        ulong u = (ulong)a;
        o[v + 18] = (long)(u << s);
        o[v + 19] = (long)(u >> s);
        o[v + 20] = (long)(u >>> s);

        // int mixed with long is computed in long
        int n = (int)b;
        o[v + 21] = a + n;
        o[v + 22] = n * a;
        o[v + 23] = (long)n * n;
        o[v + 24] = n * n;
        o[v + 25] = (long)(n << s);

        // A long target with an int value: the operator is long
        long z = a;
        z += n;
        o[v + 26] = z;
        z *= n;
        o[v + 27] = z;
        z -= n;
        o[v + 28] = z;
    }

    [GpuDeviceFunction]
    public static void ConvertOperators(float f, double d, long[] o, int index)
    {
        int v = index * ConversionsPerInput;
        o[v] = (int)f;
        o[v + 1] = (uint)f;
        o[v + 2] = (long)f;
        o[v + 3] = (long)(ulong)f;
        o[v + 4] = (sbyte)f;
        o[v + 5] = (byte)f;
        o[v + 6] = (short)f;
        o[v + 7] = (ushort)f;
        o[v + 8] = (char)f;
        o[v + 9] = (int)d;
        o[v + 10] = (uint)d;
        o[v + 11] = (long)d;
        o[v + 12] = (long)(ulong)d;
        o[v + 13] = (sbyte)d;
        o[v + 14] = (byte)d;
        o[v + 15] = (short)d;
        o[v + 16] = (ushort)d;

        // A conversion of a product: (int)(f * 0.5f), and of a float that a double was rounded to
        o[v + 17] = (int)(f * 0.5f) + (int)(float)d;
    }

    // Thread t takes ints[t / ints.Length], ints[t % ints.Length] and shifts[t % shifts.Length]
    [GpuKernel]
    public static void IntTriplesKernel(int[] ints, int[] shifts, int[] o)
    {
        int t = ThreadIndex.X;
        IntOperators(ints[t / ints.Length], ints[t % ints.Length], shifts[t % shifts.Length], o, t);
    }

    [GpuKernel]
    public static void LongTriplesKernel(long[] longs, int[] shifts, long[] o)
    {
        int t = ThreadIndex.X;
        LongOperators(longs[t / longs.Length], longs[t % longs.Length], shifts[t % shifts.Length], o, t);
    }

    [GpuKernel]
    public static void ConversionsKernel(float[] fs, double[] ds, long[] o)
    {
        int t = ThreadIndex.X;
        ConvertOperators(fs[t], ds[t], o, t);
    }

    // var takes the C# type int of the thread built-ins, not the unsigned int of threadIdx (#521)
    [GpuKernel]
    public static void SignedBuiltInsKernel(int[] negative, long[] wide)
    {
        var i = ThreadIndex.X;
        var b = BlockIndex.X;
        var d = BlockDimension.X;
        var g = GridDimension.X;
        var y = ThreadIndex.Y;
        negative[i] = (i - 1) < 0 ? 1 : 0;
        negative[2 + i] = (b - 1) < 0 ? 1 : 0;
        negative[4 + i] = (d - 3) < 0 ? 1 : 0;
        negative[6 + i] = (g - 2) < 0 ? 1 : 0;
        negative[8 + i] = (y - 1) < 0 ? 1 : 0;
        wide[i] = i - 1;
        wide[2 + i] = b - 1;
        wide[4 + i] = d - 3;
        wide[6 + i] = g - 2;
        wide[8 + i] = ThreadIndex.X - 2;
    }

    // Loop counters near the ends of int: the increment of a counter that the condition bounds never overflows, the
    // others wrap as in C#
    [GpuKernel]
    public static void LoopCountersKernel(int[] starts, int[] ends, int[] counts)
    {
        int t = ThreadIndex.X;
        int up = 0;
        for (int i = starts[t]; i < ends[t]; i++)
        {
            up++;
        }

        int down = 0;
        for (int i = ends[t]; i > starts[t]; i--)
        {
            down++;
        }

        int steps = 0;
        int last = 0;
        for (int i = starts[t]; i < ends[t]; i += 2)
        {
            steps++;
            last = i;
            if (i > ends[t] - 2)
            {
                break;
            }
        }

        counts[t * 4] = up;
        counts[t * 4 + 1] = down;
        counts[t * 4 + 2] = steps;
        counts[t * 4 + 3] = last;
    }

    // On the CPU dialects the barriers turn the kernel into a state machine; the lowered tree must keep the C# operators
    [GpuKernel]
    public static void AcrossBarrierKernel(int[] a, int[] s, int[] o)
    {
        int t = ThreadIndex.X;
        int x = a[t];
        int n = s[t];
        Gpu.SyncThreads();
        o[t * 4] = x + 1 < x ? 1 : 0;
        x += 1;
        Gpu.SyncThreads();
        o[t * 4 + 1] = x << n;
        o[t * 4 + 2] = x++;
        o[t * 4 + 3] = (int)(x * 1.0e10f);
    }
}
