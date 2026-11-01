using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms.FermatTestMath;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Property tests for the 3-prime 35-bit NTT convolution, fused middle-pass shared-memory squaring, and fused
/// division-free Garner CRT3 + 96-bit slice carry resolution in <see cref="KernelAdvancedMathHandler"/>.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class Ntt35FusedConvolutionPropertyTests
{
    // 26 words: spans limb j = 10 (bitOffset = 30, w2 bit 0 at bit 384) and limb j = 21 (bitOffset = 31, w2 bits 0..1 at bits 768..769).
    // 64 words: exercises rightShift == 64 (i = 37, j = 32), rightShift < 64, and rightShift > 64 with non-zero cHi >= 2^10.
    // 280 / 281 words: straddles the m_35 = 512 (single-block) vs m_35 = 1024 (4-step 2D fused middle-pass) threshold.
    // 561 words: m_35 = 2048 (non-square 2D split N1 = 32, N2 = 64).
    // 1121 words: m_35 = 4096 (square 2D split N1 = 64, N2 = 64, multi-block carry resolution).
    // 16384 words: m_35 = 32768 (N1 = 128, N2 = 256, 32 carry chunks).
    public static TheoryData<int> BoundaryOperandWords => [1, 26, 64, 280, 281, 561, 1121, 16384];

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TransformLengthFor35_ReducesTargetExponentTo8MillionPointsAndGuardsLimit()
    {
        Assert.Equal(2, KernelAdvancedMathHandler.TransformLengthFor35(1));
        Assert.Equal(512, KernelAdvancedMathHandler.TransformLengthFor35(560));
        Assert.Equal(1024, KernelAdvancedMathHandler.TransformLengthFor35(561));

        // E = 71,428,571 -> ProductWords = 8,928,572 -> TransformLength shrinks 4x from 2^25 to 2^23 (8,388,608).
        var targetModulus = new QuadraticFoldModulus(71_428_571);
        Assert.Equal(1 << 23, KernelAdvancedMathHandler.TransformLengthFor35(targetModulus.ProductWords));

        // MaxExponent = 134,217,727 -> ProductWords = 2^24 -> TransformLength = 2^24.
        Assert.Equal(1 << 24, KernelAdvancedMathHandler.TransformLengthFor35(1 << 24));
        Assert.Throws<ArgumentException>(() => KernelAdvancedMathHandler.TransformLengthFor35((1 << 24) + 1));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Unpack35_ThreeWordWindowWitnesses_MatchBigIntegerSquareAndMultiply()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        // Test both single-block (32 words -> m_35 = 64) and 2D transposed (300 words -> m_35 = 1024) unpack paths.
        int[] sizes = [32, 300];
        foreach (var words in sizes)
        {
            using var workspace = new GpuWorkspace(2 * words);
            var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
            using var scratch = KernelAdvancedMathHandler.NttMultiplyScratch.Allocate(workspace, handler, 2 * words);
            using var result = new GpuBigUInt(workspace, 2 * words);

            // Limb j = 10 starts at bit 350 (word 10, bitOffset = 30) and ends at bit 384 (word 12, bit 0 = w2 bit 0).
            // Limb j = 21 starts at bit 735 (word 22, bitOffset = 31) and ends at bits 768..769 (word 24, bits 0..1 = w2 bits 0..1).
            var witnessValues = new[]
            {
                BigInteger.One << 384,
                (BigInteger.One << 768) | (BigInteger.One << 769),
                (BigInteger.One << 350) | (BigInteger.One << 384) | (BigInteger.One << 735) | (BigInteger.One << 769) | 1
            };

            foreach (var value in witnessValues)
            {
                var operandWords = ToWords(value, words);
                using var operand = new GpuBigUInt(workspace, operandWords);

                handler.Square(workspace, operand, result, scratch);

                // P-3word-window: bits residing in the third 32-bit word w2 at bitOffset in {30, 31} survive unpack and convolution.
                // mutant: ExecuteUnpack35Transposed `| (w2 << (64 - bitOffset))` removed (drops bit 34 at bitOffset=30 and bits 33..34 at bitOffset=31)
                Assert.Equal(value * value, FromWords(result.ToHostWords(workspace)));
            }
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Carry35_HundredLimbAllOnesRippleAcrossTilesAndSegments_MatchesBigInteger()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        // 300 words -> m_35 = 1024 (N1 = 32, N2 = 32: each 32-limb tile/segment is 35 output words).
        const int Words = 300;
        using var workspace = new GpuWorkspace(2 * Words);
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
        using var scratch = KernelAdvancedMathHandler.NttMultiplyScratch.Allocate(workspace, handler, 2 * Words);
        using var result = new GpuBigUInt(workspace, 2 * Words);

        // 2^35 - 1 = 31 * 1,108,307,721. Setting limbs j = 1..100 of A to 1,108,307,721, limb j = 0 to 2,000,000,000,
        // and B = 31 produces CRT coefficients X_0 = 2^35 + 27,640,261,632 (outgoing carry 1) and X_1..X_100 = 2^35 - 1
        // (zero upper bits, all 35 lower bits set), forcing a 1-bit ripple carry across 100 consecutive 35-bit limbs
        // (3,500 bits > 3 tiles and > 3 segments).
        const ulong LimbUnit = ((1UL << 35) - 1UL) / 31UL;
        BigInteger aVal = 2_000_000_000UL;
        for (var j = 1; j <= 100; j++)
        {
            aVal += new BigInteger(LimbUnit) << (35 * j);
        }

        BigInteger bVal = 31;
        using var a = new GpuBigUInt(workspace, ToWords(aVal, Words));
        using var b = new GpuBigUInt(workspace, ToWords(bVal, Words));

        handler.Multiply(workspace, a, b, result, scratch);

        // P-carry35-ripple: 1-bit ripple carry propagates across all 32 lanes of a warp, across 35-word tile boundaries, and across N2-limb segment boundaries.
        // mutant: ResolveNtt35CarriesTiled32x32 omits WarpScanGp or omits BlockCarries[tileIdx] cross-tile carry application
        Assert.Equal(aVal * bVal, FromWords(result.ToHostWords(workspace)));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(BoundaryOperandWords))]
    public void FusedSquareAndMultiply_AcrossThresholdsAnd96BitSlices_MatchBigInteger(int words)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(2 * words);
        var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
        using var scratch = KernelAdvancedMathHandler.NttMultiplyScratch.Allocate(workspace, handler, 2 * words);
        using var squareResult = new GpuBigUInt(workspace, 2 * words);
        using var multiplyResult = new GpuBigUInt(workspace, 2 * words);
        var random = new Random(701 * 100_000 + words);

        // Case 0: All-ones words (every interior 35-bit limb equals 2^35 - 1 > Prime1 > Prime2 > Prime3, forcing initial
        // Barrett reduction in Unpack35 and non-zero 96-bit upper words cHi across rightShift < 64, == 64, > 64).
        // Case 1: Deterministic random dense operand.
        var allOnes = new uint[words];
        Array.Fill(allOnes, uint.MaxValue);
        var randomA = RandomWords(random, words);
        var randomB = RandomWords(random, words);

        uint[][] squareInputs = [allOnes, randomA];
        for (var idx = 0; idx < squareInputs.Length; idx++)
        {
            var xWords = squareInputs[idx];
            var xVal = FromWords(xWords);
            using var x = new GpuBigUInt(workspace, xWords);

            handler.Square(workspace, x, squareResult, scratch);
            handler.Multiply(workspace, x, x, multiplyResult, scratch);

            var actualSquare = FromWords(squareResult.ToHostWords(workspace));
            var actualMultiply = FromWords(multiplyResult.ToHostWords(workspace));
            var expectedSquare = xVal * xVal;

            // P-fused-square-exact: fused middle-pass Square agrees with unfused Multiply(x, x) and BigInteger x^2.
            // mutant: ExecuteNttFusedMiddleSquareRow in-shared bit-reversal or ApplyTwiddleAfter skipped
            // mutant: ExecuteResolveNtt35CarriesBlocked `| ((ulong)cHi << (64 - rightShift))` removed
            Assert.True(expectedSquare == actualSquare, $"Square mismatch: words={words} case={idx}");
            Assert.True(expectedSquare == actualMultiply, $"Multiply(x,x) mismatch: words={words} case={idx}");
        }

        using var left = new GpuBigUInt(workspace, randomA);
        using var right = new GpuBigUInt(workspace, randomB);
        handler.Multiply(workspace, left, right, multiplyResult, scratch);

        var expectedProduct = FromWords(randomA) * FromWords(randomB);
        Assert.True(expectedProduct == FromWords(multiplyResult.ToHostWords(workspace)),
            $"Multiply(A,B) mismatch: words={words}");
    }

    private static uint[] RandomWords(Random random, int words)
    {
        var bytes = new byte[words * 4];
        random.NextBytes(bytes);
        var result = new uint[words];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }
}
