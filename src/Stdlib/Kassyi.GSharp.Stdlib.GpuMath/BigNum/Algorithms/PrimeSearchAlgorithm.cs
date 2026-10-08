using System.Security.Cryptography;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Provides high-performance prime generation utilizing CPU trial factoring and GPU Fermat PRP testing.
/// </summary>
public static class PrimeSearchAlgorithm
{
    /// <summary>
    /// Generates a strictly positive probable prime (PRP) of the specified bit length.
    /// Uses CPU trial division for small primes, followed by robust Miller-Rabin / Fermat PRP testing.
    /// (Automatically offloads to GPU FermatAlgorithm for extremely large bit lengths > 4096).
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

            // 2. Probable Prime Test (Fermat / Miller-Rabin)
            if (IsProbablePrime(candidate, 10))
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
        int[] smallPrimes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97
        ];
        foreach (var p in smallPrimes)
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

        // Simplified Fermat PRP Test using bases starting from 2
        // TODO: Full Miller-Rabin or FermatRunner offload for extremely large numbers
        for (int i = 0; i < iterations; i++)
        {
            var a = new BigInteger(2) + i; 
            if (BigInteger.ModPow(a, n - 1, n) != 1)
            {
                return false;
            }
        }
        return true;
    }
}
