using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Stdlib.Math.Algorithms;

/// <summary>
/// 3-Prime NTT (Number Theoretic Transform) and Garner's CRT combination for O(N log N) large integer multiplication.
/// CPU reference implementation designed for validation before GPU porting.
/// </summary>
public static class NttCpuReference
{
    // The three Solinas-like Proth primes chosen for 64-bit non-overflowing arithmetic
    public const uint P1 = 2130706433; // 2^24 * 127 + 1
    public const uint P2 = 1224736769; // 73 * 2^24 + 1
    public const uint P3 = 754974721; // 45 * 2^24 + 1

    public const uint G1 = 3;
    public const uint G2 = 3;
    public const uint G3 = 11;

    // ModPow for uint
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModPow(uint baseValue, uint exponent, uint modulus)
    {
        ulong result = 1;
        ulong currentBase = baseValue;
        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
            {
                result = (result * currentBase) % modulus;
            }

            currentBase = (currentBase * currentBase) % modulus;
            exponent >>= 1;
        }

        return (uint)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModInverse(uint value, uint modulus) => ModPow(value, modulus - 2, modulus);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModAdd(uint operandA, uint operandB, uint modulus)
    {
        var result = operandA + operandB;
        if (result >= modulus)
        {
            result -= modulus;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModSub(uint operandA, uint operandB, uint modulus)
    {
        if (operandA < operandB)
        {
            return operandA + modulus - operandB;
        }

        return operandA - operandB;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul(uint operandA, uint operandB, uint modulus) => (uint)(((ulong)operandA * operandB) % modulus);

    public static void Ntt(uint[] values, uint modulus, uint primitiveRoot, bool isInverse)
    {
        var length = values.Length;
        var numberOfBits = 0;
        while ((1 << numberOfBits) < length)
        {
            numberOfBits++;
        }

        if ((1 << numberOfBits) != length)
        {
            throw new ArgumentException("Length must be a power of 2");
        }

        // Bit reversal permutation
        for (var index = 0; index < length; index++)
        {
            var reversedIndex = 0;
            for (var bitIndex = 0; bitIndex < numberOfBits; bitIndex++)
            {
                if ((index & (1 << bitIndex)) != 0)
                {
                    reversedIndex |= (1 << (numberOfBits - 1 - bitIndex));
                }
            }

            if (index < reversedIndex)
            {
                (values[index], values[reversedIndex]) = (values[reversedIndex], values[index]);
            }
        }

        // Roots of unity
        for (var currentLength = 2; currentLength <= length; currentLength <<= 1)
        {
            var halfLength = currentLength >> 1;
            var rootOfUnity = ModPow(primitiveRoot, (modulus - 1) / (uint)currentLength, modulus);
            if (isInverse)
            {
                rootOfUnity = ModInverse(rootOfUnity, modulus);
            }

            for (var blockIndex = 0; blockIndex < length; blockIndex += currentLength)
            {
                uint twiddleFactor = 1;
                for (var elementIndex = 0; elementIndex < halfLength; elementIndex++)
                {
                    var leftTerm = values[blockIndex + elementIndex];
                    var rightTerm = ModMul(values[blockIndex + halfLength + elementIndex], twiddleFactor, modulus);
                    values[blockIndex + elementIndex] = ModAdd(leftTerm, rightTerm, modulus);
                    values[blockIndex + halfLength + elementIndex] = ModSub(leftTerm, rightTerm, modulus);
                    twiddleFactor = ModMul(twiddleFactor, rootOfUnity, modulus);
                }
            }
        }

        if (isInverse)
        {
            var lengthInverse = ModInverse((uint)length, modulus);
            for (var index = 0; index < length; index++)
            {
                values[index] = ModMul(values[index], lengthInverse, modulus);
            }
        }
    }

    /// <summary>
    /// Multiplies two arrays of 32-bit unsigned integers using 3-Prime NTT and Garner's Algorithm.
    /// </summary>
    public static uint[] Multiply(uint[] operandA, uint[] operandB)
    {
        var combinedLength = operandA.Length + operandB.Length;
        var transformLength = 1;
        while (transformLength < combinedLength)
        {
            transformLength <<= 1;
        }

        var operandA1 = new uint[transformLength];
        var operandA2 = new uint[transformLength];
        var operandA3 = new uint[transformLength];
        var operandB1 = new uint[transformLength];
        var operandB2 = new uint[transformLength];
        var operandB3 = new uint[transformLength];

        for (var index = 0; index < operandA.Length; index++)
        {
            operandA1[index] = operandA[index] % P1;
            operandA2[index] = operandA[index] % P2;
            operandA3[index] = operandA[index] % P3;
        }

        for (var index = 0; index < operandB.Length; index++)
        {
            operandB1[index] = operandB[index] % P1;
            operandB2[index] = operandB[index] % P2;
            operandB3[index] = operandB[index] % P3;
        }

        Ntt(operandA1, P1, G1, false);
        Ntt(operandA2, P2, G2, false);
        Ntt(operandA3, P3, G3, false);

        Ntt(operandB1, P1, G1, false);
        Ntt(operandB2, P2, G2, false);
        Ntt(operandB3, P3, G3, false);

        var result1 = new uint[transformLength];
        var result2 = new uint[transformLength];
        var result3 = new uint[transformLength];

        for (var index = 0; index < transformLength; index++)
        {
            result1[index] = ModMul(operandA1[index], operandB1[index], P1);
            result2[index] = ModMul(operandA2[index], operandB2[index], P2);
            result3[index] = ModMul(operandA3[index], operandB3[index], P3);
        }

        Ntt(result1, P1, G1, true);
        Ntt(result2, P2, G2, true);
        Ntt(result3, P3, G3, true);

        // Garner's Algorithm to reconstruct C
        // X = x1 + x2*P1 + x3*P1*P2
        // X = result1 (mod P1) -> x1 = result1
        // X = result2 (mod P2) -> x1 + x2*P1 = result2 (mod P2) -> x2 = (result2 - x1) * inv(P1, P2) (mod P2)
        // X = result3 (mod P3) -> x1 + x2*P1 + x3*P1*P2 = result3 (mod P3) -> x3 = (result3 - x1 - x2*P1) * inv(P1*P2, P3) (mod P3)

        var inverseP1ModP2 = ModInverse(P1, P2);
        var inverseP1P2ModP3 = ModInverse((uint)(((ulong)P1 * P2) % P3), P3);

        var finalResult = new uint[transformLength];
        ulong carry = 0;

        for (var index = 0; index < transformLength; index++)
        {
            ulong x1 = result1[index];

            var diff2 = result2[index] >= x1 % P2 ? result2[index] - x1 % P2 : result2[index] + P2 - x1 % P2;
            var x2 = (diff2 * inverseP1ModP2) % P2;

            var term12 = (x1 + x2 * P1) % P3;
            var diff3 = result3[index] >= term12 ? result3[index] - term12 : result3[index] + P3 - term12;
            var x3 = (diff3 * inverseP1P2ModP3) % P3;

            ulong p1 = P1;
            ulong p2 = P2;

            var combinedValue = (UInt128)x1 + (UInt128)x2 * p1 + (UInt128)x3 * p1 * p2;
            var sum = combinedValue + carry;
            finalResult[index] = (uint)(sum & 0xFFFFFFFF);
            carry = (ulong)(sum >> 32);
        }

        return finalResult;
    }
}



