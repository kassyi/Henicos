using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

/// <summary>
/// <see cref="BigInteger"/> references of the <see cref="FermatAlgorithm"/> tests, computed from the test's own data and
/// never read back from the code under test.
/// </summary>
/// <remarks>
/// Symbols: <c>N(E) = 2^(2E+1) - 2^E + 11</c>, <c>m = 2E + 1</c>, <c>c = 2^E - 11</c>.
/// </remarks>
internal static class FermatTestMath
{
    /// <summary>Returns <c>N(E) = 2^(2E+1) - 2^E + 11</c>.</summary>
    public static BigInteger ModulusOf(int e) => (BigInteger.One << (2 * e + 1)) - (BigInteger.One << e) + 11;

    /// <summary>Returns one fold <c>F(x) = (x mod 2^m) + floor(x / 2^m) * c</c>, written directly from its definition.</summary>
    public static BigInteger Fold(int e, BigInteger x)
    {
        var m = 2 * e + 1;
        var c = (BigInteger.One << e) - 11;
        return (x & ((BigInteger.One << m) - 1)) + (x >> m) * c;
    }

    /// <summary>Returns the state after <paramref name="k"/> squarings of <paramref name="base"/>: <c>a^(2^k) mod N</c>.</summary>
    public static BigInteger Squarings(int e, uint @base, long k) =>
        BigInteger.ModPow(@base, BigInteger.One << checked((int)k), ModulusOf(e));

    /// <summary>
    /// Returns the reference verdict and residues of a Fermat PRP test of <c>N(E)</c> to base <paramref name="base"/>:
    /// <c>a^(N-1) = 1 (mod N)</c>, and the low 64 bits of <c>a^(2^(2E+1) + 10) mod N</c> and of <c>a^(2^E) mod N</c>.
    /// </summary>
    public static (bool IsProbablePrime, ulong Residue64, ulong CheckResidue64) PrpReference(int e, uint @base)
    {
        var n = ModulusOf(e);
        var isProbablePrime = BigInteger.ModPow(@base, n - 1, n).IsOne;
        var residue = BigInteger.ModPow(@base, (BigInteger.One << (2 * e + 1)) + 10, n);
        var check = BigInteger.ModPow(@base, BigInteger.One << e, n);
        return (isProbablePrime, Low64(residue), Low64(check));
    }

    /// <summary>Returns the low 64 bits of a non-negative value.</summary>
    public static ulong Low64(BigInteger value) => (ulong)(value & ulong.MaxValue);

    /// <summary>Returns a seeded random value in <c>[0, bound)</c>.</summary>
    public static BigInteger RandomBelow(Random random, BigInteger bound)
    {
        var bytes = new byte[bound.GetByteCount(isUnsigned: true) + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        return new BigInteger(bytes, isUnsigned: true) % bound;
    }

    /// <summary>Returns <paramref name="value"/> as <paramref name="words"/> little-endian 32-bit words.</summary>
    public static uint[] ToWords(BigInteger value, int words)
    {
        var bytes = new byte[words * 4];
        Assert.True(value.TryWriteBytes(bytes, out _, isUnsigned: true), $"{value} does not fit {words} words.");
        var result = new uint[words];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    /// <summary>Reads little-endian 32-bit words as an unsigned integer.</summary>
    public static BigInteger FromWords(ReadOnlySpan<uint> words) =>
        new(MemoryMarshal.AsBytes(words), isUnsigned: true, isBigEndian: false);
}

/// <summary>An <see cref="IProgress{T}"/> that runs its handler on the reporting thread, so that tests are deterministic.</summary>
internal sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
