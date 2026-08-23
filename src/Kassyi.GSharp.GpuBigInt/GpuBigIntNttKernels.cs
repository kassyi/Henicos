using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.GpuBigInt;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntNttKernels
{
    // NTT Primes and primitive roots
    // We use two 32-bit primes for CRT to support convolution values up to 2^55
    // P1 = 63 * 2^26 + 1 (Proth prime)
    public const uint NttP1 = 4253024257; // 素数 (507 * 2^23 + 1, or something similar)
    public const uint NttG1 = 5; // 原始根

    // NTT素数 2
    public const uint NttP2 = 4293918721; // 素数 (4095 * 2^20 + 1)
    public const uint NttG2 = 19; // 原始根

    // Barrett Reduction Constants
    // M = floor(2^64 / P)
    // M1 = floor(2^64 / 4253024257) = 4337323974
    public const ulong NttM1 = 4337323974UL; // for P1
    // M2 = floor(2^64 / 4293918721) = 4296016127
    public const ulong NttM2 = 4296016127UL; // for P2

    [GpuDevice]
    public static uint ModAdd(uint a, uint b, uint p)
    {
        var res = a + b;
        if (res >= p || res < a)
        {
            res -= p;
        }

        return res;
    }

    [GpuDevice]
    public static uint ModSub(uint a, uint b, uint p)
    {
        var res = a - b;
        if (a < b)
        {
            res += p;
        }

        return res;
    }
    // ReSharper disable UselessBinaryOperation
    // ReSharper disable ConditionIsAlwaysTrueOrFalse
    [GpuDevice, GpuInline]
    public static uint ModMulBarrett(uint a, uint b, uint p, ulong m)
    {
        ulong prod = 0;
        Gpu.InlinePtx($"asm volatile(\"mul.wide.u32 %0, %1, %2;\" : \"=l\"({prod}) : \"r\"({a}), \"r\"({b}));");

        ulong q = 0;
        Gpu.InlinePtx($"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({q}) : \"l\"({prod}), \"l\"({m}));");

        var res = prod - q * p;
        if (res >= p)
        {
            res -= p;
        }

        return (uint)res;
    }
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
    // ReSharper restore UselessBinaryOperation

    [GpuDevice]
    public static uint ModMul1(uint a, uint b) => ModMulBarrett(a, b, NttP1, NttM1);

    [GpuDevice]
    public static uint ModMul2(uint a, uint b) => ModMulBarrett(a, b, NttP2, NttM2);

    [GpuDevice]
    public static uint ModPow(uint baseVal, uint exp, uint p, ulong m)
    {
        uint res = 1;
        baseVal = baseVal % p;
        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                res = ModMulBarrett(res, baseVal, p, m);
            }

            baseVal = ModMulBarrett(baseVal, baseVal, p, m);
            exp >>= 1;
        }

        return res;
    }

    [GpuDevice]
    public static void AtomicAddWithCarry(uint[] address, int wordIdx, uint val, int maxWords)
    {
        var carry = val;
        var idx = wordIdx;
        while (carry > 0 && idx < maxWords)
        {
            var old = Gpu.AtomicAdd(ref address[idx], carry);
            if (old + carry < old)
            {
                carry = 1;
                idx++;
            }
            else
            {
                break;
            }
        }
    }
}
