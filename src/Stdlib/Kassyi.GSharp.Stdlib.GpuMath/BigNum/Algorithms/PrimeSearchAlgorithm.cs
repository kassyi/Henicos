using System.Security.Cryptography;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Generates probable primes on the CPU with trial division and the Miller-Rabin test.
/// </summary>
public static class PrimeSearchAlgorithm
{
    private const int DefaultMillerRabinIterations = 10;

    private static ReadOnlySpan<int> SmallPrimes =>
    [
        2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97
    ];

    /// <summary>
    /// Generates a strictly positive probable prime (PRP) of the specified bit length.
    /// Uses CPU trial division by small primes, followed by 10 rounds of the Miller-Rabin test with
    /// <see cref="BigInteger"/>, for every bit length; nothing runs on the GPU.
    /// </summary>
    /// <param name="bitLength">The required bit length of the prime.</param>
    /// <returns>A generated probable prime of the specified bit length.</returns>
    public static BigInteger Generate(int bitLength)
    {
        if (bitLength < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), "Bit length must be at least 2.");
        }

        while (true)
        {
            var candidate = GenerateRandomOdd(bitLength);

            // 1. Fast Trial Factoring (Sieve out multiples of small primes)
            if (!IsPrimeCpu(candidate))
            {
                continue;
            }

            // 2. Probable Prime Test (Miller-Rabin)
            if (IsProbablePrime(candidate, DefaultMillerRabinIterations))
            {
                return candidate;
            }
        }
    }

    private static BigInteger GenerateRandomOdd(int bitLength)
    {
        var bytes = new byte[(bitLength + 7) / 8];
        RandomNumberGenerator.Fill(bytes);

        // Ensure MSB is set for exact bit length
        var topBitsToKeep = bitLength % 8;
        if (topBitsToKeep != 0)
        {
            var mask = (byte)((1 << topBitsToKeep) - 1);
            bytes[^1] &= mask;
            bytes[^1] |= (byte)(1 << (topBitsToKeep - 1));
        }
        else
        {
            bytes[^1] |= 0x80;
        }

        // Ensure it's odd
        bytes[0] |= 0x01;

        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static bool IsPrimeCpu(BigInteger n)
    {
        // Trial division with first 25 primes
        foreach (var p in SmallPrimes)
        {
            if (n % p == 0)
            {
                return n == p;
            }
        }

        return true;
    }

    private static bool IsProbablePrime(BigInteger n, int iterations)
    {
        if (n <= 1)
        {
            return false;
        }

        if (n == 2 || n == 3)
        {
            return true;
        }

        if (n.IsEven)
        {
            return false;
        }

        // Decompose n - 1 = 2^s * d with d odd
        var d = n - 1;
        var s = 0;
        while (d.IsEven)
        {
            d >>= 1;
            s++;
        }

        for (var i = 0; i < iterations; i++)
        {
            var a = new BigInteger(2 + i);
            if (a >= n - 1)
            {
                break;
            }

            if (!IsMillerRabinWitnessPass(a, d, s, n))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMillerRabinWitnessPass(BigInteger a, BigInteger d, int s, BigInteger n)
    {
        var x = BigInteger.ModPow(a, d, n);
        if (x == 1 || x == n - 1)
        {
            return true;
        }

        for (var r = 1; r < s; r++)
        {
            x = BigInteger.ModPow(x, 2, n);
            if (x == n - 1)
            {
                return true;
            }
        }

        return false;
    }
}
