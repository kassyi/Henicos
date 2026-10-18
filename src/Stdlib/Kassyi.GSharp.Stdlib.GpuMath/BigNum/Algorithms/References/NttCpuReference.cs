namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.References;

/// <summary>
/// Provides a host (CPU) reference implementation of 3-Prime Number Theoretic Transform (NTT) and Garner's Chinese Remainder Theorem (CRT) reconstruction.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Employs Solinas-like Proth primes ($P_1, P_2, P_3$) for 64-bit non-overflowing arithmetic during NTT convolution,
/// providing an exact CPU validation reference for <c>GpuBigIntNttKernels</c>.
/// </para>
/// <para>
/// <b>Execution Target:</b> Host-only (CPU) reference calculation.
/// </para>
/// </remarks>
internal static class NttCpuReference
{

    // [WHY] Solinas-like Proth primes chosen for 64-bit non-overflowing arithmetic in NTT.
    /// <summary>First NTT prime modulus ($P_1 = 2^{24} \times 127 + 1$).</summary>
    public const uint P1 = 2130706433; // 2^24 * 127 + 1
    /// <summary>Second NTT prime modulus ($P_2 = 73 \times 2^{24} + 1$).</summary>
    public const uint P2 = 1224736769; // 73 * 2^24 + 1
    /// <summary>Third NTT prime modulus ($P_3 = 45 \times 2^{24} + 1$).</summary>
    public const uint P3 = 754974721; // 45 * 2^24 + 1

    /// <summary>Primitive root of unity for <see cref="P1"/>.</summary>
    public const uint G1 = 3;
    /// <summary>Primitive root of unity for <see cref="P2"/>.</summary>
    public const uint G2 = 3;
    /// <summary>Primitive root of unity for <see cref="P3"/>.</summary>
    public const uint G3 = 11;

    private static readonly uint s_inverseP1ModP2 = ModInverse(P1, P2);
    private static readonly uint s_inverseP1P2ModP3 = ModInverse((uint)(((ulong)P1 * P2) % P3), P3);

    /// <summary>Computes modular exponentiation (baseValue^exponent % modulus).</summary>
    /// <param name="baseValue">The base value.</param>
    /// <param name="exponent">The exponent.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>The result of the modular exponentiation.</returns>
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

    /// <summary>Computes the modular multiplicative inverse using Fermat's Little Theorem.</summary>
    /// <param name="value">The value to invert.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>The modular inverse of the value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModInverse(uint value, uint modulus) => ModPow(value, modulus - 2, modulus);

    /// <summary>Computes modular addition (a + b % modulus) without overflow.</summary>
    /// <param name="operandA">The first operand.</param>
    /// <param name="operandB">The second operand.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>The result of the modular addition.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModAdd(uint operandA, uint operandB, uint modulus)
    {
        var sum = (ulong)operandA + operandB;
        return (uint)(sum >= modulus ? sum - modulus : sum);
    }

    /// <summary>Computes modular subtraction (a - b % modulus) with wrap-around handling.</summary>
    /// <param name="operandA">The first operand.</param>
    /// <param name="operandB">The second operand.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>The result of the modular subtraction.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModSub(uint operandA, uint operandB, uint modulus)
    {
        if (operandA < operandB)
        {
            return (uint)((ulong)operandA + modulus - operandB);
        }

        return operandA - operandB;
    }

    /// <summary>Computes modular multiplication (a * b % modulus) via 64-bit intermediate arithmetic.</summary>
    /// <param name="operandA">The first operand.</param>
    /// <param name="operandB">The second operand.</param>
    /// <param name="modulus">The modulus.</param>
    /// <returns>The result of the modular multiplication.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul(uint operandA, uint operandB, uint modulus) => (uint)(((ulong)operandA * operandB) % modulus);

    /// <summary>Performs in-place forward or inverse Number Theoretic Transform on power-of-two buffer.</summary>
    /// <param name="values">The array of values to transform in-place.</param>
    /// <param name="modulus">The prime modulus.</param>
    /// <param name="primitiveRoot">The primitive root of unity.</param>
    /// <param name="isInverse">If <c>true</c>, performs the inverse NTT; otherwise, the forward NTT.</param>
    public static void Ntt(uint[] values, uint modulus, uint primitiveRoot, bool isInverse)
    {
        ArgumentNullException.ThrowIfNull(values);

        var length = values.Length;
        if (!BitOperations.IsPow2(length))
        {
            throw new ArgumentException("Length must be a power of 2.", nameof(values));
        }

        var numberOfBits = BitOperations.Log2((uint)length);
        BitReversePermute(values, length, numberOfBits);

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

    private static void BitReversePermute(uint[] values, int length, int numberOfBits)
    {
        for (var index = 0; index < length; index++)
        {
            var reversedIndex = 0;
            for (var bitIndex = 0; bitIndex < numberOfBits; bitIndex++)
            {
                if ((index & (1 << bitIndex)) != 0)
                {
                    reversedIndex |= 1 << (numberOfBits - 1 - bitIndex);
                }
            }

            if (index < reversedIndex)
            {
                (values[index], values[reversedIndex]) = (values[reversedIndex], values[index]);
            }
        }
    }

    private static void ExecuteNttPass(uint[] operandA, uint[] operandB, uint[] result, uint prime, uint primitiveRoot, int transformLength)
    {
        var opA = new uint[transformLength];
        var opB = new uint[transformLength];

        for (var index = 0; index < operandA.Length; index++)
        {
            opA[index] = operandA[index] % prime;
        }

        for (var index = 0; index < operandB.Length; index++)
        {
            opB[index] = operandB[index] % prime;
        }

        Ntt(opA, prime, primitiveRoot, false);
        Ntt(opB, prime, primitiveRoot, false);

        for (var index = 0; index < transformLength; index++)
        {
            result[index] = ModMul(opA[index], opB[index], prime);
        }

        Ntt(result, prime, primitiveRoot, true);
    }

    /// <summary>Multiplies two arrays of 32-bit unsigned integers using 3-Prime NTT and Garner's CRT algorithm.</summary>
    /// <param name="operandA">The first operand array.</param>
    /// <param name="operandB">The second operand array.</param>
    /// <returns>An array containing the product.</returns>
    // ReSharper disable once UnusedMember.Global -- External API used by test assemblies
    public static uint[] Multiply(uint[] operandA, uint[] operandB)
    {
        ArgumentNullException.ThrowIfNull(operandA);
        ArgumentNullException.ThrowIfNull(operandB);

        var combinedLength = operandA.Length + operandB.Length;
        var transformLength = combinedLength <= 1 ? 1 : (int)BitOperations.RoundUpToPowerOf2((uint)combinedLength);

        var result1 = new uint[transformLength];
        var result2 = new uint[transformLength];
        var result3 = new uint[transformLength];

        ExecuteNttPass(operandA, operandB, result1, P1, G1, transformLength);
        ExecuteNttPass(operandA, operandB, result2, P2, G2, transformLength);
        ExecuteNttPass(operandA, operandB, result3, P3, G3, transformLength);

        // X = x1 + x2*P1 + x3*P1*P2
        return GarnerReconstruct(result1, result2, result3, transformLength, s_inverseP1ModP2, s_inverseP1P2ModP3);
    }

    private static uint[] GarnerReconstruct(uint[] result1, uint[] result2, uint[] result3, int transformLength, uint inverseP1ModP2, uint inverseP1P2ModP3)
    {
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

            var combinedValue = x1 + (UInt128)x2 * p1 + (UInt128)x3 * p1 * p2;
            var sum = combinedValue + carry;
            finalResult[index] = (uint)(sum & 0xFFFFFFFF);
            carry = (ulong)(sum >> 32);
        }

        return finalResult;
    }
}




