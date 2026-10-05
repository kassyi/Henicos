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
}

