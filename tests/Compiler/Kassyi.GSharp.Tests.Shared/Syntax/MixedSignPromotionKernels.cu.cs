// ReSharper disable All
// The bitwise operators on sign-extended operands are what the test checks
#pragma warning disable CS0675
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Binary operators that mix <see langword="uint"/> with <see langword="int"/>, <see langword="short"/> or
/// <see langword="sbyte"/>. C# converts both operands to <see langword="long"/> (C# spec §12.4.7.3); the usual arithmetic
/// conversions of C++ pick <c>unsigned int</c>, so the transpiler writes the C# conversions (#671). Each thread takes one
/// pair of inputs; <see cref="MixedSignPromotionScenarios"/> compares the results with the same device functions run as
/// C# on the host.
/// </summary>
public static partial class MixedSignPromotionKernels
{
    /// <summary>The <see langword="long"/> results that one pair of operands writes.</summary>
    public const int ValuesPerPair = 18;

    /// <summary>The comparison results (0 or 1) that one pair of operands writes.</summary>
    public const int FlagsPerPair = 12;

    [GpuDeviceFunction]
    public static void IntOperators(uint u, int n, long[] values, int[] flags, int pair)
    {
        int v = pair * ValuesPerPair;
        values[v] = u + n;
        values[v + 1] = u - n;
        values[v + 2] = u * n;
        values[v + 3] = u / n;
        values[v + 4] = u % n;
        values[v + 5] = u & n;
        values[v + 6] = u | n;
        values[v + 7] = u ^ n;
        values[v + 8] = n + u;
        values[v + 9] = n - u;
        values[v + 10] = n * u;
        values[v + 11] = u != 0 ? n / u : 0;
        values[v + 12] = u != 0 ? n % u : 0;
        values[v + 13] = n & u;
        values[v + 14] = n | u;
        values[v + 15] = n ^ u;

        // var is auto in C++: the declared type must be long long as well
        var sum = u + n;
        values[v + 16] = sum;

        // u & n never exceeds uint, so only an operation on the result shows its type: (u & n) - 1 is -1 for u = 0
        values[v + 17] = (u & n) - 1;

        int f = pair * FlagsPerPair;
        flags[f] = u == n ? 1 : 0;
        flags[f + 1] = u != n ? 1 : 0;
        flags[f + 2] = u < n ? 1 : 0;
        flags[f + 3] = u > n ? 1 : 0;
        flags[f + 4] = u <= n ? 1 : 0;
        flags[f + 5] = u >= n ? 1 : 0;
        flags[f + 6] = n == u ? 1 : 0;
        flags[f + 7] = n != u ? 1 : 0;
        flags[f + 8] = n < u ? 1 : 0;
        flags[f + 9] = n > u ? 1 : 0;
        flags[f + 10] = n <= u ? 1 : 0;
        flags[f + 11] = n >= u ? 1 : 0;
    }

    [GpuDeviceFunction]
    public static void ShortOperators(uint u, short s, long[] values, int[] flags, int pair)
    {
        int v = pair * ValuesPerPair;
        values[v] = u + s;
        values[v + 1] = u - s;
        values[v + 2] = u * s;
        values[v + 3] = u / s;
        values[v + 4] = u % s;
        values[v + 5] = u & s;
        values[v + 6] = u | s;
        values[v + 7] = u ^ s;
        values[v + 8] = s + u;
        values[v + 9] = s - u;
        values[v + 10] = s * u;
        values[v + 11] = u != 0 ? s / u : 0;
        values[v + 12] = u != 0 ? s % u : 0;
        values[v + 13] = s & u;
        values[v + 14] = s | u;
        values[v + 15] = s ^ u;
        var sum = u + s;
        values[v + 16] = sum;
        values[v + 17] = (u & s) - 1;

        int f = pair * FlagsPerPair;
        flags[f] = u == s ? 1 : 0;
        flags[f + 1] = u != s ? 1 : 0;
        flags[f + 2] = u < s ? 1 : 0;
        flags[f + 3] = u > s ? 1 : 0;
        flags[f + 4] = u <= s ? 1 : 0;
        flags[f + 5] = u >= s ? 1 : 0;
        flags[f + 6] = s == u ? 1 : 0;
        flags[f + 7] = s != u ? 1 : 0;
        flags[f + 8] = s < u ? 1 : 0;
        flags[f + 9] = s > u ? 1 : 0;
        flags[f + 10] = s <= u ? 1 : 0;
        flags[f + 11] = s >= u ? 1 : 0;
    }

    [GpuDeviceFunction]
    public static void SByteOperators(uint u, sbyte b, long[] values, int[] flags, int pair)
    {
        int v = pair * ValuesPerPair;
        values[v] = u + b;
        values[v + 1] = u - b;
        values[v + 2] = u * b;
        values[v + 3] = u / b;
        values[v + 4] = u % b;
        values[v + 5] = u & b;
        values[v + 6] = u | b;
        values[v + 7] = u ^ b;
        values[v + 8] = b + u;
        values[v + 9] = b - u;
        values[v + 10] = b * u;
        values[v + 11] = u != 0 ? b / u : 0;
        values[v + 12] = u != 0 ? b % u : 0;
        values[v + 13] = b & u;
        values[v + 14] = b | u;
        values[v + 15] = b ^ u;
        var sum = u + b;
        values[v + 16] = sum;
        values[v + 17] = (u & b) - 1;

        int f = pair * FlagsPerPair;
        flags[f] = u == b ? 1 : 0;
        flags[f + 1] = u != b ? 1 : 0;
        flags[f + 2] = u < b ? 1 : 0;
        flags[f + 3] = u > b ? 1 : 0;
        flags[f + 4] = u <= b ? 1 : 0;
        flags[f + 5] = u >= b ? 1 : 0;
        flags[f + 6] = b == u ? 1 : 0;
        flags[f + 7] = b != u ? 1 : 0;
        flags[f + 8] = b < u ? 1 : 0;
        flags[f + 9] = b > u ? 1 : 0;
        flags[f + 10] = b <= u ? 1 : 0;
        flags[f + 11] = b >= u ? 1 : 0;
    }

    // Thread pair = i * ns.Length + j takes us[i] and ns[j]
    [GpuKernel]
    public static void IntPairsKernel(uint[] us, int[] ns, long[] values, int[] flags)
    {
        int pair = ThreadIndex.X;
        IntOperators(us[pair / ns.Length], ns[pair % ns.Length], values, flags, pair);
    }

    [GpuKernel]
    public static void ShortPairsKernel(uint[] us, short[] ss, long[] values, int[] flags)
    {
        int pair = ThreadIndex.X;
        ShortOperators(us[pair / ss.Length], ss[pair % ss.Length], values, flags, pair);
    }

    [GpuKernel]
    public static void SBytePairsKernel(uint[] us, sbyte[] bs, long[] values, int[] flags)
    {
        int pair = ThreadIndex.X;
        SByteOperators(us[pair / bs.Length], bs[pair % bs.Length], values, flags, pair);
    }

    // A non-negative int constant converts to uint (C# spec §10.2.11), so u - 8 stays uint; a negative one does not
    [GpuDeviceFunction]
    public static void ConstantOperators(uint u, long[] values, int[] flags, int index)
    {
        values[index * 3] = u - 8;
        values[index * 3 + 1] = u + -8;
        values[index * 3 + 2] = u + 8u;
#pragma warning disable CS0652 // The comparison is always false in C#, which is what the test checks
        flags[index] = u == -1 ? 1 : 0;
#pragma warning restore CS0652
    }

    [GpuKernel]
    public static void ConstantOperandsKernel(uint[] us, long[] values, int[] flags)
    {
        int tid = ThreadIndex.X;
        ConstantOperators(us[tid], values, flags, tid);
    }

    // Returns 3 and lowers k by 5: TakeFive(ref k) + k interferes, so its operands go through temporaries (#608)
    [GpuDeviceFunction]
    public static uint TakeFive(ref int k)
    {
        k -= 5;
        return 3u;
    }

    // On the CPU dialects the barriers turn the kernel into a state machine, whose lowered tree the conversions must
    // still be read from
    [GpuKernel]
    public static void AcrossBarrierKernel(uint[] us, int[] ns, long[] values, int[] flags)
    {
        int tid = ThreadIndex.X;
        uint u = us[tid];
        int n = ns[tid];
        Gpu.SyncThreads();
        long sum = u + n;
        Gpu.SyncThreads();
        values[tid * 2] = sum;
        flags[tid] = u < n ? 1 : 0;
        int k = tid;
        values[tid * 2 + 1] = TakeFive(ref k) + k;
    }
}
