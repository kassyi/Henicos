// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// 64-bit Goldilocks prime (<c>P = 2^64 - 2^32 + 1</c>) constants and modular arithmetic device functions.
/// </summary>
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GoldilocksArithmetic
{
    public const ulong GoldilocksPrime = 0xFFFFFFFF00000001UL;
    public const ulong GoldilocksEpsilon = 0xFFFFFFFFUL;
    public const ulong GoldilocksTwoAdicRoot = 7277203076849721926UL;

    [GpuDeviceFunction, GpuInline]
    public static ulong GoldilocksAdd(ulong operandA, ulong operandB)
    {
        ulong sum = operandA + operandB;
        ulong carry = sum < operandA ? GoldilocksEpsilon : 0UL;
        ulong res = sum + carry;
        if (res < sum)
        {
            res += GoldilocksEpsilon;
        }

        if (res >= GoldilocksPrime)
        {
            res -= GoldilocksPrime;
        }

        return res;
    }

    [GpuDeviceFunction, GpuInline]
    public static ulong GoldilocksSub(ulong operandA, ulong operandB)
    {
        ulong diff = operandA - operandB;
        ulong borrow = operandA < operandB ? GoldilocksEpsilon : 0UL;
        ulong res = diff - borrow;
        if (diff < borrow)
        {
            res -= GoldilocksEpsilon;
        }

        if (res >= GoldilocksPrime)
        {
            res -= GoldilocksPrime;
        }

        return res;
    }

    [GpuDeviceFunction, GpuInline]
    public static ulong GoldilocksMul(ulong operandA, ulong operandB)
    {
        ulong a0 = operandA & GoldilocksEpsilon;
        ulong a1 = operandA >> 32;
        ulong b0 = operandB & GoldilocksEpsilon;
        ulong b1 = operandB >> 32;

        ulong p00 = a0 * b0;
        ulong p01 = a0 * b1;
        ulong p10 = a1 * b0;
        ulong p11 = a1 * b1;

        ulong mid = (p00 >> 32) + (p01 & GoldilocksEpsilon) + (p10 & GoldilocksEpsilon);
        ulong xLo = (p00 & GoldilocksEpsilon) | (mid << 32);
        ulong xHi = p11 + (p01 >> 32) + (p10 >> 32) + (mid >> 32);

        ulong xHiHi = xHi >> 32;
        ulong xHiLo = xHi & GoldilocksEpsilon;

        ulong t0 = xLo - xHiHi;
        if (xLo < xHiHi)
        {
            t0 -= GoldilocksEpsilon;
        }

        ulong t1 = (xHiLo << 32) - xHiLo;

        ulong res = t0 + t1;
        if (res < t0)
        {
            res += GoldilocksEpsilon;
        }

        if (res >= GoldilocksPrime)
        {
            res -= GoldilocksPrime;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static ulong GoldilocksPow(ulong baseValue, ulong exponent)
    {
        ulong result = 1UL;
        baseValue %= GoldilocksPrime;
        while (exponent > 0UL)
        {
            if ((exponent & 1UL) != 0UL)
            {
                result = GoldilocksMul(result, baseValue);
            }

            baseValue = GoldilocksMul(baseValue, baseValue);
            exponent >>= 1;
        }

        return result;
    }
}

