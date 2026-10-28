namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Host-only tests of <see cref="QuadraticFoldModulus"/>, the modulus N = 2^(2E+1) - 2^E + 11 of
/// <see cref="FermatAlgorithm"/> and its reference fold. Every expected value comes from <see cref="BigInteger"/>.
/// </summary>
/// <remarks>
/// Symbols as in the class: m = 2E + 1, c = 2^E - 11 (N = 2^m - c), L = ceil(m / 32) words.
/// </remarks>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class QuadraticFoldModulusTests
{
    // E = 4 is the smallest exponent with c > 0; 31 / 32 / 47 / 48 / 63 / 64 / 65 put m = 2E + 1 and the shift by E
    // on both sides of a word boundary (E % 32 = 31 / 0 / 15 / 16 / 31 / 0 / 1); 1000 is a many-word modulus.
    public static TheoryData<int> FoldExponents => [4, 31, 32, 47, 48, 63, 64, 65, 1000];

    public static TheoryData<int> CanonicalizeExponents => [1, 3, 4, 32, 65, 1000];

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(FoldExponents))]
    public void FoldReference_RandomSquares_AreCongruentAndCanonicalizeToTheResidue(int e)
    {
        var modulus = new QuadraticFoldModulus(e);
        var n = ModulusOf(e);
        var m = 2 * e + 1;
        var random = new Random(532 * 1000 + e);

        for (var sample = 0; sample < 16; sample++)
        {
            // u < 2^(m+1) is the state invariant the fold keeps, so u covers the whole input range of a squaring.
            var u = RandomBelow(random, BigInteger.One << (m + 1));
            var square = u * u;
            var result = new uint[modulus.Words];

            modulus.FoldReference(ToWords(square, modulus.ProductWords), result);

            // The result is exactly F(F(F(x))) of the definition, not only some congruent value.
            // mutant: QuadraticFoldModulus.cs FoldOnce `carry = sum >> 32` -> `carry = 0`
            var folded = FromWords(result);
            Assert.True(Fold(e, Fold(e, Fold(e, square))) == folded, $"E={e} sample={sample}: not F(F(F(x))).");

            // The fold is congruent to its input modulo N.
            // mutant: QuadraticFoldModulus.cs FoldOnce `ConstantTerm` (t = 11) -> 10 (c = 2^E - 10)
            Assert.True(folded % n == square % n, $"E={e} seed={532 * 1000 + e} sample={sample}: fold is not congruent.");

            // Output bound (P-bound): u < 2^(m+1) gives x3 < 2^m + 2^(E+1), which is below 2N.
            Assert.True(folded < (BigInteger.One << m) + (BigInteger.One << (e + 1)),
                $"E={e} sample={sample}: fold output {folded} exceeds 2^m + 2^(E+1).");

            // One conditional subtraction gives the canonical residue in [0, N).
            modulus.Canonicalize(result);
            Assert.Equal(square % n, FromWords(result));
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FoldExponents))]
    public void FoldReference_LargestState_StaysWithinTheBoundAfterThreePasses(int e)
    {
        var modulus = new QuadraticFoldModulus(e);
        var n = ModulusOf(e);
        var m = 2 * e + 1;

        // The largest state u = 2^(m+1) - 1 gives the largest square; after two passes x2 can reach about 3 * 2^m, so
        // only the third pass brings it under 2^m + 2^(E+1).
        var u = (BigInteger.One << (m + 1)) - 1;
        var square = u * u;
        var result = new uint[modulus.Words];

        modulus.FoldReference(ToWords(square, modulus.ProductWords), result);

        // P-bound: x3 < 2^m + 2^(E+1), and the value is exactly F(F(F(x))).
        // mutant: QuadraticFoldModulus.cs FoldReference third FoldOnce removed (x2 copied to the result)
        var folded = FromWords(result);
        Assert.Equal(Fold(e, Fold(e, Fold(e, square))), folded);
        Assert.True(folded < (BigInteger.One << m) + (BigInteger.One << (e + 1)),
            $"E={e}: fold output {folded} exceeds 2^m + 2^(E+1).");
        Assert.Equal(square % n, folded % n);

        modulus.Canonicalize(result);
        Assert.Equal(square % n, FromWords(result));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(CanonicalizeExponents))]
    public void Canonicalize_ValuesBelowTwiceTheModulus_ReturnTheResidue(int e)
    {
        var modulus = new QuadraticFoldModulus(e);
        var n = ModulusOf(e);
        BigInteger[] values = [BigInteger.Zero, BigInteger.One, n - 1, n, n + 1, 2 * n - 1];

        foreach (var value in values)
        {
            var words = ToWords(value, modulus.Words);

            // The canonical form is the residue in [0, N); N maps to 0 and 2N - 1 to N - 1.
            // mutant: QuadraticFoldModulus.cs Canonicalize `CompareTo(...) >= 0` -> `> 0` (N stays N)
            modulus.Canonicalize(words);
            Assert.Equal(value % n, FromWords(words));
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(1000)]
    public void Constructor_DescribesTheModulus(int e)
    {
        var modulus = new QuadraticFoldModulus(e);
        var m = 2 * e + 1;

        // The modulus words are N = 2^(2E+1) - 2^E + 11, also for E <= 3 where 11 carries into bit m.
        Assert.Equal(ModulusOf(e), FromWords(modulus.Modulus));
        Assert.Equal(e, modulus.E);
        Assert.Equal(m, modulus.ModulusBits);

        // L = ceil(m / 32) words hold a state u < 2^(m+1) (m is odd, so 32L >= m + 1).
        Assert.Equal((m + 31) / 32, modulus.Words);
        Assert.True(32 * modulus.Words >= m + 1);
        Assert.Equal(2 * modulus.Words, modulus.ProductWords);

        // The fold widths cover the pass bounds: m + E + 3 bits after the first pass, m + 2 after the second.
        Assert.Equal((m + e + 3 + 31) / 32, modulus.FirstFoldWords);
        Assert.Equal((m + 2 + 31) / 32, modulus.SecondFoldWords);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Constructor_LargestExponent_FillsTheNttProductLimit()
    {
        // E = 2^27 - 1 is the largest exponent: L = 2^23 words and a 2^24-word product, the NTT length limit (#529).
        // mutant: QuadraticFoldModulus.cs ThrowIfExponentOutOfRange `exponent > MaxExponent` -> `>=`
        var modulus = new QuadraticFoldModulus(QuadraticFoldModulus.MaxExponent);

        Assert.Equal(1 << 23, modulus.Words);
        Assert.Equal(1 << 24, modulus.ProductWords);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(QuadraticFoldModulus.MaxExponent + 1)]
    [InlineData(int.MaxValue)]
    public void Constructor_ExponentOutOfRange_Throws(int e)
    {
        // E = 0 gives N = 12 (even); above MaxExponent the product exceeds the NTT length, and 2E + 1 would overflow
        // int for int.MaxValue, which must be rejected before any arithmetic.
        // mutant: QuadraticFoldModulus.cs ThrowIfExponentOutOfRange upper check removed
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new QuadraticFoldModulus(e));
        Assert.Equal("exponent", exception.ParamName);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1)]
    [InlineData(3)]
    public void FoldReference_ExponentBelowFour_Throws(int e)
    {
        // c = 2^E - 11 is negative for E <= 3, so the fold is not defined there (#532-1); FermatAlgorithm uses the host
        // stage for those exponents.
        // mutant: QuadraticFoldModulus.cs FoldReference E check removed
        var modulus = new QuadraticFoldModulus(e);
        var product = new uint[modulus.ProductWords];
        var result = new uint[modulus.Words];

        Assert.Throws<InvalidOperationException>(() => modulus.FoldReference(product, result));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FoldReference_And_Canonicalize_WrongLengths_Throw()
    {
        var modulus = new QuadraticFoldModulus(40);

        Assert.Throws<ArgumentException>(() => modulus.FoldReference(new uint[modulus.ProductWords - 1], new uint[modulus.Words]));
        Assert.Throws<ArgumentException>(() => modulus.FoldReference(new uint[modulus.ProductWords], new uint[modulus.Words + 1]));
        Assert.Throws<ArgumentException>(() => modulus.Canonicalize(new uint[modulus.Words - 1]));
    }
}
