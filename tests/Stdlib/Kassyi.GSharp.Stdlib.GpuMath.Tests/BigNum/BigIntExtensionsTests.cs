namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Host-side limb conversions of <see cref="BigIntExtensions"/>.
/// </summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class BigIntExtensionsTests
{
    /// <summary>
    /// Named inputs for <see cref="BigIntExtensions.WriteLimbs"/>: the value's word count and the destination length.
    /// </summary>
    public static TheoryData<string> WriteLimbsInputs() =>
    [
        // 9 words (36 bytes) into 4: the first TryWriteBytes fails and the copy goes through the 512-byte stack buffer.
        "NineWordsIntoFour",
        // Exactly 512 bytes with the top bit set: fits the stack buffer only when written unsigned.
        "TopBitSet128WordsIntoFour",
        // 200 words (800 bytes): too large for the stack buffer, so the bytes go through ArrayPool.
        "TwoHundredWordsIntoFour",
        "ExactFit",
        "SmallValueIntoPrefilled",
        "NegativeValueWritesMagnitude",
        "Zero",
        "EmptyDestination",
    ];

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(WriteLimbsInputs))]
    public void WriteLimbs_KeepsTheLowWordsOfTheMagnitude(string input)
    {
        var (value, destinationWords) = BuildWriteLimbsInput(input);
        var destination = Enumerable.Repeat(uint.MaxValue, destinationWords).ToArray();

        value.WriteLimbs(destination);

        // The destination holds |value| mod 2^(32 * length), little-endian, with every other word cleared.
        // mutant: BigIntegerExtensions.cs:88 `if (TryWriteBytes(...))` negated (NineWordsIntoFour)
        // mutant: BigIntegerExtensions.cs:95 `neededBytes <= 512` -> `> 512` / `true ?` (TwoHundredWordsIntoFour)
        // mutant: BigIntegerExtensions.cs:101 `!TryWriteBytes` -> `TryWriteBytes`, `isUnsigned: false` (TopBitSet128WordsIntoFour), `isBigEndian: true`
        // mutant: BigIntegerExtensions.cs:106 `Math.Min` -> `Math.Max`; :107 CopyTo removed; :111 `!= null` -> `== null`
        var modulus = BigInteger.One << (32 * destinationWords);
        Assert.Equal(ToWords(BigInteger.Abs(value) % modulus, destinationWords), destination);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new uint[] { 5u, 0u }, true, "-5")]
    [InlineData(new uint[] { 5u, 0u }, false, "5")]
    [InlineData(new uint[] { 0u, 1u }, true, "-4294967296")]
    [InlineData(new uint[] { 0u }, true, "0")]
    [InlineData(new uint[0], true, "0")]
    public void ToBigInteger_IsNegative_NegatesMagnitude(uint[] words, bool isNegative, string expected)
    {
        // The words are an unsigned little-endian magnitude; isNegative negates it.
        // mutant: BigIntegerExtensions.cs:70 `isNegative ? -big : big` -> `big` / `-big`
        Assert.Equal(BigInteger.Parse(expected), BigIntExtensions.ToBigInteger(words, isNegative));
    }

    /// <summary>
    /// Named inputs for <see cref="BigIntExtensions.Sqrt"/>: the values around a perfect square <c>r²</c> (the root
    /// moves by one between <c>r² - 1</c> and <c>r²</c>, and <c>r² + 2r</c> has the largest remainder), for
    /// <c>r = 2^64 + 2^33 + 1</c> and a 300-bit <c>r</c>; powers of two of both parities; the smallest values; and seeded
    /// random values.
    /// </summary>
    public static TheoryData<string> SqrtInputs() =>
    [
        "Zero", "One", "Two", "Three", "Four",
        "SquareMinusOne", "Square", "SquarePlusOne", "SquarePlusTwoR",
        "WideSquareMinusOne", "WideSquare", "WideSquarePlusTwoR",
        "PowerOfTwoEven", "PowerOfTwoOdd",
        "Random64", "Random500", "Random1000",
    ];

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtInputs))]
    public void Sqrt_IsFloorOfSquareRoot(string input)
    {
        var value = BuildSqrtInput(input);

        var root = value.Sqrt();

        // r = floor(sqrt(v)) by the definition r² <= v < (r + 1)², and the same r as the test's own integer square root.
        // The CPU big-float Sqrt and Rsqrt and the CPU InverseSqrt rely on it being exact.
        // mutant: BigIntegerExtensions.cs:42 `root * root > value` -> `root * root >= value` (every perfect square: One, Four,
        // Square, WideSquare, PowerOfTwoEven)
        Assert.True(root * root <= value && value < (root + 1) * (root + 1), $"{input}: {root} is not floor(sqrt({value}))");
        Assert.Equal(BigFloatReference.IntegerSqrt(value), root);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Sqrt_Negative_Throws() =>
        // The square root of a negative value is not defined.
        Assert.Throws<ArgumentOutOfRangeException>(() => BigInteger.MinusOne.Sqrt());

    private static BigInteger BuildSqrtInput(string input)
    {
        var r = (BigInteger.One << 64) + (BigInteger.One << 33) + 1;
        var wideR = (BigInteger.One << 299) + (BigInteger.One << 150) + 12345;
        var random = new Random(661);
        BigInteger RandomBits(int bits)
        {
            var bytes = new byte[(bits + 7) / 8 + 1];
            random.NextBytes(bytes);
            bytes[^1] = 0;
            return new BigInteger(bytes) >> (8 * (bytes.Length - 1) - bits) | (BigInteger.One << (bits - 1));
        }

        return input switch
        {
            "Zero" => BigInteger.Zero,
            "One" => BigInteger.One,
            "Two" => new BigInteger(2),
            "Three" => new BigInteger(3),
            "Four" => new BigInteger(4),
            "SquareMinusOne" => r * r - 1,
            "Square" => r * r,
            "SquarePlusOne" => r * r + 1,
            "SquarePlusTwoR" => r * r + 2 * r,
            "WideSquareMinusOne" => wideR * wideR - 1,
            "WideSquare" => wideR * wideR,
            "WideSquarePlusTwoR" => wideR * wideR + 2 * wideR,
            "PowerOfTwoEven" => BigInteger.One << 200,
            "PowerOfTwoOdd" => BigInteger.One << 201,
            "Random64" => RandomBits(64),
            "Random500" => RandomBits(500),
            "Random1000" => RandomBits(1000),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown Sqrt input."),
        };
    }

    private static (BigInteger Value, int DestinationWords) BuildWriteLimbsInput(string input)
    {
        var random = new Random(598);
        BigInteger RandomValue(int words, bool topBit)
        {
            var limbs = new uint[words];
            for (var i = 0; i < words; i++)
            {
                limbs[i] = (uint)random.NextInt64(0, 1L << 32);
            }

            if (topBit)
            {
                limbs[^1] |= 0x8000_0000u;
            }

            return FromWords(limbs);
        }

        return input switch
        {
            "NineWordsIntoFour" => (RandomValue(9, topBit: true), 4),
            "TopBitSet128WordsIntoFour" => (RandomValue(128, topBit: true), 4),
            "TwoHundredWordsIntoFour" => (RandomValue(200, topBit: true), 4),
            "ExactFit" => (RandomValue(4, topBit: true), 4),
            "SmallValueIntoPrefilled" => (new BigInteger(12345), 4),
            "NegativeValueWritesMagnitude" => (-RandomValue(9, topBit: false), 4),
            "Zero" => (BigInteger.Zero, 4),
            "EmptyDestination" => (RandomValue(2, topBit: true), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown WriteLimbs input."),
        };
    }

    private static BigInteger FromWords(uint[] words)
    {
        var value = BigInteger.Zero;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | words[i];
        }

        return value;
    }

    private static uint[] ToWords(BigInteger value, int count)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)(value & uint.MaxValue);
            value >>= 32;
        }

        return words;
    }
}
