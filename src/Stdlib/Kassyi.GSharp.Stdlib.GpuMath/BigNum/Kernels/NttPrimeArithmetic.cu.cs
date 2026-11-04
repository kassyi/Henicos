// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// 32-bit NTT prime moduli (<c>k * 2^25 + 1</c>), Barrett reduction constants, Garner CRT3 constants, and modular
/// arithmetic device functions.
/// </summary>
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class NttPrimeArithmetic
{
    public const uint Prime1 = 4194304001u; // 125 * 2^25 + 1
    public const uint NttG1 = 3u;
    public const ulong BarrettMagic1 = 4398046510UL;

    public const uint Prime2 = 2717908993u; // 81 * 2^25 + 1
    public const uint NttG2 = 5u;
    public const ulong BarrettMagic2 = 6787108810UL;

    public const uint Prime3 = 2113929217u; // 63 * 2^25 + 1
    public const uint NttG3 = 5u;
    public const ulong BarrettMagic3 = 8726282755UL;

    // Division-free 3-prime Garner CRT constants (M = P1 * P2 * P3 ~ 2^94.28)
    public const uint Prime1InvModPrime2 = 1050101200u;
    public const uint Prime1Prime2InvModPrime3 = 733056103u;
    public const uint Prime1ModPrime3 = 2080374784u; // Prime1 % Prime3
    public const ulong Prime1TimesPrime2 = 11399736563693780993UL; // (ulong)Prime1 * Prime2

    /// <summary>Pairs a 32-bit NTT prime modulus with its 64-bit Barrett reduction constant <c>floor(2^64 / P)</c>.</summary>
    public struct NttPrimeModulus
    {
        public uint P;
        public ulong Pm;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModAdd(uint operandA, uint operandB, uint primeModulus)
    {
        if (operandA >= primeModulus - operandB)
        {
            return operandA - (primeModulus - operandB);
        }

        return operandA + operandB;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModSub(uint operandA, uint operandB, uint primeModulus)
    {
        var result = operandA - operandB;
        if (operandA < operandB)
        {
            result += primeModulus;
        }

        return result;
    }

    // ReSharper disable once ConditionIsAlwaysTrueOrFalse -- Variables are modified via inline PTX assembly which ReSharper cannot detect
    // ReSharper disable once UselessBinaryOperation -- Variables are modified via inline PTX assembly which ReSharper cannot detect
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMulBarrett(uint operandA, uint operandB, uint primeModulus, ulong barrettMagic)
    {
        ulong product = 0;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.wide.u32 %0, %1, %2;\" : \"=l\"({product}) : \"r\"({operandA}), \"r\"({operandB}));");

        ulong quotient = 0;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({quotient}) : \"l\"({product}), \"l\"({barrettMagic}));");

        var remainder = product - quotient * primeModulus;
        if (remainder >= primeModulus)
        {
            remainder -= primeModulus;
        }

        return (uint)remainder;
    }
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
    // ReSharper restore UselessBinaryOperation

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul(uint operandA, uint operandB, NttPrimeModulus modulus) =>
        ModMulBarrett(operandA, operandB, modulus.P, modulus.Pm);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul1(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime1, BarrettMagic1);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul2(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime2, BarrettMagic2);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul3(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime3, BarrettMagic3);

    // ReSharper disable once ConditionIsAlwaysTrueOrFalse -- Variables are modified via inline PTX assembly
    // ReSharper disable once UselessBinaryOperation -- Variables are modified via inline PTX assembly
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModReduce64Barrett(ulong value, uint primeModulus, ulong barrettMagic)
    {
        ulong quotient = 0UL;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({quotient}) : \"l\"({value}), \"l\"({barrettMagic}));");

        ulong remainder = value - quotient * primeModulus;
        if (remainder >= primeModulus)
        {
            remainder -= primeModulus;
        }

        return (uint)remainder;
    }

    [GpuDeviceFunction]
    public static uint ModPow(uint baseValue, uint exponent, uint primeModulus, ulong barrettMagic)
    {
        uint result = 1;
        baseValue = baseValue % primeModulus;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result = ModMulBarrett(result, baseValue, primeModulus, barrettMagic);
            }

            baseValue = ModMulBarrett(baseValue, baseValue, primeModulus, barrettMagic);
            exponent >>= 1;
        }

        return result;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModPow(uint baseValue, uint exponent, NttPrimeModulus modulus) =>
        ModPow(baseValue, exponent, modulus.P, modulus.Pm);
}

