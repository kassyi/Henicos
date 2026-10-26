
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntModPowTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ModPow_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var e = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var m = new GpuBigUInt(ctx, new uint[] { 5, 0, 0, 0 });
        
        using var result = new GpuBigUInt(ctx, 4);
        
        GpuBigIntDivRem.ModPow(a, e, m, result);
        
        Assert.Equal(2u, result.ToHostArray()[0]); // 3^3 mod 5 = 27 mod 5 = 2
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ModPow_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        const int NumWords = 64; // 2048 bits (> 1024 PolyAlgorithmThreshold -> ModPowZeroSyncController)
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[NumWords];
        var eArr = new uint[NumWords];
        var mArr = new uint[NumWords];
        Array.Fill(aArr, 0x11111111u);
        Array.Fill(mArr, 0x7FFFFFFFu);
        mArr[0] |= 1; // odd modulus

        // Multi-word exponent with an intermediate zero word and trailing zero words
        // to cover both word-skip continuation and early-exit branches in ModPowZeroSyncController.
        eArr[0] = 0x00000015u;
        eArr[1] = 0u;
        eArr[2] = 0x00000003u;

        using var a = new GpuBigUInt(ctx, aArr);
        using var e = new GpuBigUInt(ctx, eArr);
        using var m = new GpuBigUInt(ctx, mArr);
        using var result = new GpuBigUInt(ctx, NumWords);

        var expected = BigInteger.ModPow(a.ToBigInteger(), e.ToBigInteger(), m.ToBigInteger());

        GpuBigIntDivRem.ModPow(a, e, m, result);

        Assert.Equal(expected, result.ToBigInteger());
    }

    // Every bit of the top exponent word counts, up to its most significant bit (ZeroSync path, 64 words).
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ModPow_LargeValues_NonZeroTopExponentWord()
    {
        CudaCapability.EnsureGpuAvailable();
        const int NumWords = 64; // > PolyAlgorithmThreshold -> ModPowZeroSyncController
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[NumWords];
        var mArr = new uint[NumWords];
        Array.Fill(aArr, 0x11111111u);
        Array.Fill(mArr, 0x7FFFFFFFu);
        mArr[0] |= 1;
        // A 2-word exponent whose top word has its highest bit set.
        uint[] eArr = [0x00000015u, 0x80000003u];

        var baseValue = FromWords(aArr);
        var exponent = FromWords(eArr);
        var modulus = FromWords(mArr);

        using var a = new GpuBigUInt(ctx, aArr);
        using var e = new GpuBigUInt(ctx, eArr);
        using var m = new GpuBigUInt(ctx, mArr);
        using var result = new GpuBigUInt(ctx, NumWords);

        // mutant: ModPowZeroSyncController.cs:108 `e == 0 && i == expWords - 1` -> `e != 0 && ...`
        // (the last word would stop after its first bit)
        GpuBigIntDivRem.ModPow(a, e, m, result);

        Assert.Equal(BigInteger.ModPow(baseValue, exponent, modulus), result.ToBigInteger());
    }

    /// <summary>
    /// Named moduli for the boundary tests: <c>1</c> (every residue is 0), <c>2</c> (the smallest modulus that keeps
    /// <c>1</c>) and a seeded random modulus that fills every word.
    /// </summary>
    public enum ModulusKind
    {
        One,
        Two,
        Large
    }

    // 16 words (512 bits) takes the NTT MegaKernel path (<= PolyAlgorithmThreshold of 1024 bits); 64 words (2048 bits)
    // takes the ZeroSync path. Each path writes its starting result 1 mod m on the device, so each needs modulus 1.
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16, ModulusKind.One)]
    [InlineData(16, ModulusKind.Two)]
    [InlineData(16, ModulusKind.Large)]
    [InlineData(64, ModulusKind.One)]
    [InlineData(64, ModulusKind.Two)]
    [InlineData(64, ModulusKind.Large)]
    public void Test_ModPow_ExponentZero_MatchesBigInteger(int numWords, ModulusKind modulusKind)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        var modulusValue = CreateModulus(modulusKind, numWords, seed: 637 + numWords);
        // The base stays below the modulus except for modulus 1, where only 0 is below it (#637 used base 5).
        var baseValue = modulusKind == ModulusKind.Two ? BigInteger.One : new BigInteger(5);

        using var a = new GpuBigUInt(ctx, ToWords(baseValue, numWords));
        using var e = new GpuBigUInt(ctx, ToWords(BigInteger.Zero, numWords));
        using var m = new GpuBigUInt(ctx, ToWords(modulusValue, numWords));
        using var result = new GpuBigUInt(ctx, numWords);

        GpuBigIntDivRem.ModPow(a, e, m, result);

        // x^0 mod m is 1 mod m: 0 for m = 1, 1 otherwise. The expected value comes from the inputs the test holds.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.ModPow.cu.cs:225 `idx == 0 ? oneModulo : 0u` -> `idx == 0 ? 1u : 0u`
        // input: (16, One) (NTT MegaKernel path)
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.ModPow.cu.cs:35 `result[0] = OneModulo(modulus, numWords);` -> `result[0] = 1u;`
        // input: (64, One) (ZeroSync path)
        Assert.Equal(BigInteger.ModPow(baseValue, BigInteger.Zero, modulusValue), result.ToBigInteger());
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16)]
    [InlineData(64)]
    public void Test_ModPow_ModulusOne_NonZeroExponent_ReturnsZero(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        var baseValue = new BigInteger(5);
        // Multi-word exponent so both paths run more than one word of the square-and-multiply loop.
        var exponentValue = (BigInteger.One << 40) + 3;

        using var a = new GpuBigUInt(ctx, ToWords(baseValue, numWords));
        using var e = new GpuBigUInt(ctx, ToWords(exponentValue, numWords));
        using var m = new GpuBigUInt(ctx, ToWords(BigInteger.One, numWords));
        using var result = new GpuBigUInt(ctx, numWords);

        GpuBigIntDivRem.ModPow(a, e, m, result);

        // Every integer is 0 modulo 1.
        Assert.Equal(BigInteger.ModPow(baseValue, exponentValue, BigInteger.One), result.ToBigInteger());
    }

    private static BigInteger CreateModulus(ModulusKind kind, int numWords, int seed)
    {
        switch (kind)
        {
            case ModulusKind.One:
                return BigInteger.One;
            case ModulusKind.Two:
                return new BigInteger(2);
            default:
                var words = new uint[numWords];
                var random = new Random(seed);
                for (var i = 0; i < numWords; i++)
                {
                    words[i] = (uint)random.Next() | ((uint)random.Next(2) << 31);
                }

                // Odd and with the top bit set, like the moduli of the other ModPow tests.
                words[0] |= 1u;
                words[numWords - 1] |= 0x80000000u;
                return FromWords(words);
        }
    }

    private static uint[] ToWords(BigInteger value, int numWords)
    {
        var words = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            words[i] = (uint)(value & uint.MaxValue);
            value >>= 32;
        }

        return words;
    }

    private static BigInteger FromWords(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true);
    }
}
