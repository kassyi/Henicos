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
