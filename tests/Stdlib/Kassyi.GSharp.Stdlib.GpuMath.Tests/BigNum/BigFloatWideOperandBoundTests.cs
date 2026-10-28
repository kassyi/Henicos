#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The bounds behind the truncated wide operands of the GPU <c>Divide</c> and <c>Rsqrt</c> (#661 §4.12), checked with
/// <see cref="BigInteger"/> arithmetic only: no product code runs, so these tests hold the design to its own formulas.
/// </summary>
/// <remarks>
/// <para>
/// For <c>n</c> result words (<c>p = 32n</c> bits) the operand is cut to its top <c>K = n + 2</c> words
/// (<c>k = 32K</c> bits). The estimate from the truncated operand is the exact floor or one above it, the full-width
/// check (<c>x²·A &gt; 2^(2B)</c>, <c>Q·R &gt; N</c>) is true exactly when it is one above, and when the estimate has a
/// set bit among the bits that rounding to <c>p</c> bits discards (the low word of the inverse root, the low 2 bits of
/// the quotient) the estimate and the exact floor round to the same <c>p</c> bits, so the check can be skipped.
/// </para>
/// <para>
/// Random operands almost never land within the bound of an integer, so the cases include operands built around a
/// chosen integer: a value just below it (the estimate is one too large) and exactly on it (the estimate is right and
/// the check must not move it), with the discarded bits of that integer all zero (the case where skipping the check
/// would change the result). Every property counts the cases that reach each branch.
/// </para>
/// </remarks>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class BigFloatWideOperandBoundTests
{
    // Rsqrt of A (w words, bit length 32w or 32w - 1) for n result words: K = n + 2, j = 32(w - K), m = ceil(n/2),
    // B_t = 32(K + m), B = B_t + j/2, x_hi = floor(2^B_t / sqrt(floor(A / 2^j))), x* = floor(2^B / sqrt(A)).
    // P1: x_hi - x* is 0 or 1. P2: x_hi²·A > 2^(2B) exactly when x_hi = x* + 1. P3: x_hi has at least p + 33 bits, so its
    // low word is discarded; when that word is not 0, x_hi and x* round to the same p bits. P4: x* rounded to p bits
    // with the exponent -B is RZ_p(1 / sqrt(A)) (BigFloatReference.RzRsqrt).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RsqrtEstimate_IsExactOrOneAbove_AndTheCheckDecides(int n)
    {
        var p = 32 * n;
        var truncated = n + 2;
        var random = new Random(4120 + n);
        var counts = new Dictionary<string, int>();

        foreach (var words in RadicandWidths(n))
        {
            foreach (var radicand in Radicands(random, n, words))
            {
                var j = 32 * (words - truncated);
                var margin = (n + 1) / 2;
                var bt = 32 * (truncated + margin);
                var b = bt + j / 2;
                var high = BigFloatReference.IntegerSqrt((BigInteger.One << (2 * bt)) / (radicand >> j));
                var exact = BigFloatReference.IntegerSqrt((BigInteger.One << (2 * b)) / radicand);
                var context = $"n={n}, w={words}, A=0x{radicand:X}";

                Assert.True(high - exact == 0 || high - exact == 1, $"P1 {context}: x_hi - x* = {high - exact}");
                var tooLarge = high * high * radicand > BigInteger.One << (2 * b);
                Assert.True(tooLarge == (high == exact + 1), $"P2 {context}: check {tooLarge}, x_hi - x* = {high - exact}");
                Assert.True(high.GetBitLength() >= p + 33, $"P3 {context}: x_hi has {high.GetBitLength()} bits");
                var skipped = (high & uint.MaxValue) != 0;
                if (skipped)
                {
                    Assert.True(RoundToBits(high, p) == RoundToBits(exact, p), $"P3 {context}: skipped, but x_hi and x* round apart");
                }

                var (rounded, bits) = RoundToBits(exact, p);
                var expected = BigFloatReference.RzRsqrt(radicand, 0, p);
                Assert.True(expected == (rounded, bits - p - b), $"P4 {context}");

                Count(counts, (skipped ? "skip" : "check") + (high == exact ? "-exact" : "-oneAbove"));
            }
        }

        // Coverage witnesses: every combination of skip / check and exact / one above is reached.
        foreach (var key in new[] { "skip-exact", "skip-oneAbove", "check-exact", "check-oneAbove" })
        {
            Assert.True(counts.GetValueOrDefault(key) > 0, $"n={n}: no case reached {key}");
        }
    }

    // Divide of L by R for n result words: k = 32(n + 2), j = max(0, bits(R) - k), N = floor(L·2^s) with
    // s = bits(R) + p + 2 - bits(L), Q_hi = floor(floor(N / 2^j) / floor(R / 2^j)), Q* = floor(N / R).
    // P1: Q_hi - Q* is 0 or 1, and 0 when j = 0. P2: Q_hi·R > N exactly when Q_hi = Q* + 1. P3: Q* and Q_hi have p + 2
    // or p + 3 bits; when the low 2 bits of Q_hi are not both 0, Q_hi and Q* round to the same p bits. P4: Q* rounded to
    // p bits with the exponent -s is RZ_p(L / R) (BigFloatReference.RoundTowardZero).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DivideEstimate_IsExactOrOneAbove_AndTheCheckDecides(int n)
    {
        var p = 32 * n;
        var k = 32 * (n + 2);
        var random = new Random(4130 + n);
        var counts = new Dictionary<string, int>();

        foreach (var (dividend, divisor) in DivideOperands(random, n))
        {
            var leftBits = (int)dividend.GetBitLength();
            var rightBits = (int)divisor.GetBitLength();
            var j = Math.Max(0, rightBits - k);
            var shift = rightBits + p + 2 - leftBits;
            var scaled = shift >= 0 ? dividend << shift : dividend >> -shift;
            var high = (scaled >> j) / (divisor >> j);
            var exact = scaled / divisor;
            var context = $"n={n}, L=0x{dividend:X}, R=0x{divisor:X}";

            Assert.True(high - exact == 0 || (high - exact == 1 && j > 0), $"P1 {context}: Q_hi - Q* = {high - exact}, j = {j}");
            var tooLarge = high * divisor > scaled;
            Assert.True(tooLarge == (high == exact + 1), $"P2 {context}: check {tooLarge}, Q_hi - Q* = {high - exact}");
            var exactBits = exact.GetBitLength();
            Assert.True(
                exactBits >= p + 2 && exactBits <= p + 3 && high.GetBitLength() <= p + 3,
                $"P3 {context}: Q* has {exactBits} bits, Q_hi {high.GetBitLength()}");
            var skipped = (high & 3) != 0;
            if (skipped)
            {
                Assert.True(RoundToBits(high, p) == RoundToBits(exact, p), $"P3 {context}: skipped, but Q_hi and Q* round apart");
            }

            var (rounded, bits) = RoundToBits(exact, p);
            var expected = BigFloatReference.RoundTowardZero(dividend, divisor, p);
            Assert.True(expected == (rounded, bits - p - (long)shift), $"P4 {context}");

            Count(counts, (skipped ? "skip" : "check") + (high == exact ? "-exact" : "-oneAbove"));
        }

        foreach (var key in new[] { "skip-exact", "skip-oneAbove", "check-exact", "check-oneAbove" })
        {
            Assert.True(counts.GetValueOrDefault(key) > 0, $"n={n}: no case reached {key}");
        }
    }

    // Widths from just above K words to 16n, where the GPU truncates (from K + g words) or would with a smaller g.
    private static int[] RadicandWidths(int n) => [n + 3, n + 4, n + 6, 2 * n + 8, 16 * n + 2];

    private static IEnumerable<BigInteger> Radicands(Random random, int n, int words)
    {
        var top = BigInteger.One << (32 * words - 1);
        var second = BigInteger.One << (32 * words - 2);

        // 2^(2e) + 1: the top words are a power of four, the estimate a power of two one above the exact floor.
        yield return second + 1;
        yield return second;
        yield return top + 1;
        yield return top;
        yield return (BigInteger.One << (32 * words)) - 1;

        for (var i = 0; i < 400; i++)
        {
            yield return RandomBits(random, 32 * words - random.Next(2));
        }

        // Around a chosen x: x > 2^(B - 16w) (so A < 2^(32w)) and A = ceil(2^(2B) / x²) is the smallest radicand whose
        // inverse root is at most x: below x unless x²·A = 2^(2B). Then x* = x - 1 and the truncated estimate is x.
        var truncated = n + 2;
        var b = 32 * (truncated + (n + 1) / 2) + 16 * (words - truncated);
        var p = 32 * n;
        for (var i = 0; i < 40; i++)
        {
            var x = RandomBits(random, b - 16 * words + 1);
            var discarded = (int)x.GetBitLength() - p;
            x = i % 2 == 0
                ? x >> discarded << discarded // every discarded bit 0: skipping the check would round up
                : x | 1; // the low word is not 0: the check is skipped although x is one above
            var bound = BigInteger.One << (2 * b);
            var radicand = (bound + x * x - 1) / (x * x);
            if (radicand.GetBitLength() >= 32 * words - 1 && radicand.GetBitLength() <= 32 * words)
            {
                yield return radicand;
            }
        }
    }

    private static IEnumerable<(BigInteger Dividend, BigInteger Divisor)> DivideOperands(Random random, int n)
    {
        var p = 32 * n;
        foreach (var words in new[] { n + 2, n + 3, n + 6, 4 * n, 16 * n + 2 })
        {
            var rightBits = 32 * words;

            // R = 2^(bR - 1) + 1 over L = 2^(bR - 1): the truncated divisor is a power of two and the estimate one above.
            var power = BigInteger.One << (rightBits - 1);
            yield return (power, power + 1);
            yield return (power, power);
            yield return (power + 1, power);

            for (var i = 0; i < 300; i++)
            {
                var divisor = RandomBits(random, rightBits - random.Next(31));
                var dividendBits = 1 + random.Next(32 * (16 * n + 4));
                yield return (RandomBits(random, dividendBits), divisor);
            }

            // Around a chosen quotient q of p + 2 or p + 3 bits: N = q·R - 1 (just below q) and N = q·R (exactly q),
            // both of bits(R) + p + 2 bits, so that s = 0.
            for (var i = 0; i < 40; i++)
            {
                var divisor = RandomBits(random, rightBits - random.Next(31));
                var quotient = RandomBits(random, p + 2 + random.Next(2));
                var discarded = (int)quotient.GetBitLength() - p;
                quotient = i % 2 == 0 ? quotient >> discarded << discarded : quotient | 1;
                var product = quotient * divisor;
                foreach (var dividend in new[] { product - 1, product })
                {
                    if (dividend.GetBitLength() == divisor.GetBitLength() + p + 2)
                    {
                        yield return (dividend, divisor);
                    }
                }
            }
        }
    }

    // A random value of exactly the given bit length.
    private static BigInteger RandomBits(Random random, int bits)
    {
        var bytes = new byte[(bits + 7) / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var value = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
        return value | (BigInteger.One << (bits - 1));
    }

    // The top p bits of x and the bit length of x: the rounded significand and, relative to x, its exponent.
    private static (BigInteger Significand, long Bits) RoundToBits(BigInteger x, int p)
    {
        var bits = (long)x.GetBitLength();
        return (x >> (int)(bits - p), bits);
    }

    private static void Count(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}
