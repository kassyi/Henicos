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
    public const uint NttP1 = 0xFD800001;
    public const uint NttG1 = 5;

    // P2 = 63 * 2^26 - 6291455 ? No, 0xFD200001 = 4246732801
    // P2 = 253 * 2^24 + 1
    public const uint NttP2 = 0xFD200001;
    public const uint NttG2 = 2;

    // Barrett Reduction Constants
    // M = floor(2^64 / P)
    // M1 = floor(2^64 / 4253024257) = 4337323974
    public const ulong NttM1 = 0x102871FC6UL; // for P1
    // M2 = floor(2^64 / 4246732801) = 4343749639
    public const ulong NttM2 = 0x102E91C07UL; // for P2

    [GpuDevice, GpuInline]
    public static uint ModAdd(uint a, uint b, uint p)
    {
        var res = a + b;
        if (res >= p || res < a)
        {
            res -= p;
        }

        return res;
    }

    [GpuDevice, GpuInline]
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
        // 64-bit multiplication using PTX intrinsic (much faster than full 64-bit mul)
        ulong prod = 0;
        Gpu.InlinePtx($"asm volatile(\"mul.wide.u32 %0, %1, %2;\" : \"=l\"({prod}) : \"r\"({a}), \"r\"({b}));");

        // Barrett reduction: q = (prod * m) >> 64
        ulong q = 0;
        Gpu.InlinePtx($"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({q}) : \"l\"({prod}), \"l\"({m}));");

        // r = prod - q * p
        var r = (uint)prod - (uint)q * p;

        // Correction step
        if (r >= p)
        {
            r -= p;
        }

        return r;
    }
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
    // ReSharper restore UselessBinaryOperation

    [GpuDevice, GpuInline]
    public static uint ModMul1(uint a, uint b) => ModMulBarrett(a, b, NttP1, NttM1);

    [GpuDevice, GpuInline]
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

    [GpuDevice, GpuInline]
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
