namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Provides mathematical extension methods for <see cref="BigInteger"/>.
/// </summary>
public static class BigIntegerExtensions
{
    /// <summary>
    /// Computes the integer square root (<c>floor(sqrt(value))</c>) of the specified <see cref="BigInteger"/> using Heron's method (Newton-Raphson).
    /// </summary>
    /// <param name="value">The non-negative big integer whose integer square root is to be computed.</param>
    /// <returns>The integer square root of <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
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
}
