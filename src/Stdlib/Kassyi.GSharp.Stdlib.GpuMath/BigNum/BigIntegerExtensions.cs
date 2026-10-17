namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides mathematical extension methods for the .NET standard <see cref="BigInteger"/> type.
/// </summary>
/// <remarks>
/// <para>These extensions serve as host-side baseline implementations and high-precision reference calculations.</para>
/// <para><b>Thread Safety:</b> Static methods are pure and thread-safe.</para>
/// </remarks>
public static class BigIntExtensions
{
    /// <summary>
    /// Computes the integer square root ($\lfloor\sqrt{\text{value}}\rfloor$) of the specified <see cref="BigInteger"/> using Heron's method (Newton-Raphson iteration).
    /// </summary>
    /// <param name="value">The non-negative big integer whose integer square root is to be computed.</param>
    /// <returns>The integer square root of <paramref name="value"/>, satisfying $\text{result}^2 \le \text{value} &lt; (\text{result} + 1)^2$.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is strictly negative.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Time complexity is $O(M(N) \log N)$ where $M(N)$ is the multiplication complexity of $N$-bit numbers.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) method.</para>
    /// </remarks>
    public static BigInteger Sqrt(this BigInteger value)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Square root is not defined for negative numbers.");
        }

        if (value.IsZero)
        {
            return BigInteger.Zero;
        }

        var bitLength = (int)((value.GetBitLength() + 1) / 2);
        var root = BigInteger.One << bitLength;

        while (true)
        {
            var next = (root + value / root) >> 1;
            if (next == root || next == root + 1)
            {
                if (root * root > value)
                {
                    return root - 1;
                }

                return root;
            }

            root = next;
        }
    }

    /// <summary>
    /// Converts a little-endian span of 32-bit unsigned integer limbs into a .NET <see cref="BigInteger"/> without intermediate heap allocations.
    /// </summary>
    /// <param name="words">The read-only span of 32-bit unsigned integer limbs in little-endian order.</param>
    /// <param name="isNegative"><see langword="true"/> to negate the resulting magnitude; otherwise, <see langword="false"/>.</param>
    /// <returns>A <see cref="BigInteger"/> representing the value in <paramref name="words"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BigInteger ToBigInteger(ReadOnlySpan<uint> words, bool isNegative = false)
    {
        if (words.IsEmpty)
        {
            return BigInteger.Zero;
        }

        var byteSpan = MemoryMarshal.AsBytes(words);
        var big = new BigInteger(byteSpan, isUnsigned: true, isBigEndian: false);
        return isNegative ? -big : big;
    }

    /// <summary>
    /// Writes the absolute magnitude of a <see cref="BigInteger"/> into a destination span of 32-bit unsigned integer limbs in little-endian order, truncating or zero-padding as needed.
    /// </summary>
    /// <param name="value">The <see cref="BigInteger"/> value whose magnitude is written.</param>
    /// <param name="destination">The target limb span to receive the little-endian 32-bit words.</param>
    public static void WriteLimbs(this BigInteger value, Span<uint> destination)
    {
        destination.Clear();
        var absValue = BigInteger.Abs(value);
        if (absValue.IsZero || destination.IsEmpty)
        {
            return;
        }

        var byteSpan = MemoryMarshal.AsBytes(destination);
        if (absValue.TryWriteBytes(byteSpan, out _, isUnsigned: true, isBigEndian: false))
        {
            return;
        }

        var neededBytes = absValue.GetByteCount(isUnsigned: true);
        byte[]? rentedBytes = null;
        var tempBytes = neededBytes <= 512
            ? stackalloc byte[512]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(neededBytes));

        try
        {
            if (!absValue.TryWriteBytes(tempBytes, out var bytesWritten, isUnsigned: true, isBigEndian: false))
            {
                return;
            }

            var bytesToCopy = Math.Min(bytesWritten, byteSpan.Length);
            tempBytes[..bytesToCopy].CopyTo(byteSpan);
        }
        finally
        {
            if (rentedBytes != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }
}
