
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Collection(TestCollections.GlobalStateSequential)]
public class GSharpKernelFloatProviderTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GSharpKernelFloatProviderTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    private static double ToDouble(GpuBigUInt sig, GpuFloatResult res)
    {
        var val = (double)sig.ToBigInteger() * Math.Pow(2.0, res.Exponent);
        return res.IsNegative ? -val : val;
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Arithmetic_Add_Multiply_Divide()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = GSharpKernelFloatProvider.Instance;

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 4 });
        using var bSig = new GpuBigUInt(customCtx, new uint[] { 2 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var bOp = new GpuFloatOperand(bSig.Handle, 0, false);
        var resHandle = resSig.Handle;

        // Add: 4 + 2 = 6
        var res = arith.Add(ref execContext, in aOp, in bOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(6.0, ToDouble(resSig, res), precision: 10);

        // Multiply: 4 * 2 = 8
        res = arith.Multiply(ref execContext, in aOp, in bOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(8.0, ToDouble(resSig, res), precision: 10);

        // Divide: 4 / 2 = 2
        res = arith.Divide(ref execContext, in aOp, in bOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(2.0, ToDouble(resSig, res), precision: 8);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Arithmetic_Reciprocal_Sqrt_Rsqrt()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = GSharpKernelFloatProvider.Instance;

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 4 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var resHandle = resSig.Handle;

        // Reciprocal: 1 / 4 = 0.25
        var res = arith.Reciprocal(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(0.25, ToDouble(resSig, res), precision: 8);

        // Sqrt: sqrt(4) = 2.0
        res = arith.Sqrt(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(2.0, ToDouble(resSig, res), precision: 8);

        // Rsqrt: 1 / sqrt(4) = 0.5
        res = arith.Rsqrt(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(0.5, ToDouble(resSig, res), precision: 8);

        // Non-power-of-two: a = 9 -> sqrt(9) = 3.0, rsqrt(9) = 1/3
        using var nineSig = new GpuBigUInt(customCtx, new uint[] { 9 });
        var nineOp = new GpuFloatOperand(nineSig.Handle, 0, false);
        res = arith.Sqrt(ref execContext, in nineOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(3.0, ToDouble(resSig, res), precision: 7);

        res = arith.Rsqrt(ref execContext, in nineOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(1.0 / 3.0, ToDouble(resSig, res), precision: 7);

        // Sad path: negative Sqrt / Rsqrt and zero Rsqrt
        var negOp = new GpuFloatOperand(aSig.Handle, 0, true);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var ctx = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
            arith.Sqrt(ref ctx, in negOp, resHandle);
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            var ctx = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
            arith.Rsqrt(ref ctx, in negOp, resHandle);
        });

        using var zeroSig = new GpuBigUInt(customCtx, new uint[] { 0 });
        var zeroOp = new GpuFloatOperand(zeroSig.Handle, 0, false);
        Assert.Throws<DivideByZeroException>(() =>
        {
            var ctx = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
            arith.Rsqrt(ref ctx, in zeroOp, resHandle);
        });
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Arithmetic_Exp_Log_Pow()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = GSharpKernelFloatProvider.Instance;

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 2 });
        using var bSig = new GpuBigUInt(customCtx, new uint[] { 3 });
        using var resSig = new GpuBigUInt(customCtx, 4);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var bOp = new GpuFloatOperand(bSig.Handle, 0, false);
        var resHandle = resSig.Handle;

        // Exp: exp(2)
        var res = arith.Exp(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(Math.Exp(2.0), ToDouble(resSig, res), precision: 6);

        // Log: ln(2)
        res = arith.Log(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(Math.Log(2.0), ToDouble(resSig, res), precision: 6);

        // Pow: 2^3 = 8
        res = arith.Pow(ref execContext, in aOp, in bOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(8.0, ToDouble(resSig, res), precision: 6);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Arithmetic_Normalize_DivideByUInt32()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = GSharpKernelFloatProvider.Instance;

        using var aSig = new GpuBigUInt(customCtx, new uint[] { 10 });
        using var resSig = new GpuBigUInt(customCtx, 4);
        using var divResSig = new GpuBigUInt(customCtx, 1);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var resHandle = resSig.Handle;
        var divResHandle = divResSig.Handle;

        var res = arith.Normalize(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(10.0, ToDouble(resSig, res), precision: 10);

        var divRes = arith.DivideByUInt32(ref execContext, in aOp, 3, divResHandle);
        Assert.False(divRes.IsNegative);
        Assert.Equal(0, divRes.Exponent);
        Assert.Equal(3UL, (ulong)divResSig.ToBigInteger());
        Assert.Equal(1u, divRes.Remainder);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GSharpKernel_SignedAdd_And_TranscendentalBoundaries()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var execContext = new GpuBigIntExecutionContext(customCtx, customCtx.MemoryManager, new LegacyProviderArithmeticAdapter(), customCtx.Stream);
        var arith = GSharpKernelFloatProvider.Instance;

        using var sig3 = new GpuBigUInt(customCtx, new uint[] { 3 });
        using var sig1 = new GpuBigUInt(customCtx, new uint[] { 1 });
        using var sig0 = new GpuBigUInt(customCtx, new uint[] { 0 });
        using var resSig = new GpuBigUInt(customCtx, 4);
        var resHandle = resSig.Handle;

        // 1.5 (3 * 2^-1) + (-0.25) (-1 * 2^-2) = 1.25
        var pos1_5 = new GpuFloatOperand(sig3.Handle, -1, false);
        var neg0_25 = new GpuFloatOperand(sig1.Handle, -2, true);
        var res = arith.Add(ref execContext, in pos1_5, in neg0_25, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(1.25, ToDouble(resSig, res), precision: 10);

        // 0.25 (1 * 2^-2) + (-1.5) (-3 * 2^-1) = -1.25
        var pos0_25 = new GpuFloatOperand(sig1.Handle, -2, false);
        var neg1_5 = new GpuFloatOperand(sig3.Handle, -1, true);
        res = arith.Add(ref execContext, in pos0_25, in neg1_5, resHandle);
        Assert.True(res.IsNegative);
        Assert.Equal(-1.25, ToDouble(resSig, res), precision: 10);

        // 1.5 + (-1.5) = 0.0
        res = arith.Add(ref execContext, in pos1_5, in neg1_5, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(0, res.Exponent);
        Assert.True(resSig.IsZero());

        // exp(0) = 1.0 and exp(-1.5)
        var zeroOp = new GpuFloatOperand(sig0.Handle, 0, false);
        res = arith.Exp(ref execContext, in zeroOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(1.0, ToDouble(resSig, res), precision: 10);

        res = arith.Exp(ref execContext, in neg1_5, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(Math.Exp(-1.5), ToDouble(resSig, res), precision: 6);

        // ln(1.0) = 0.0 and ln(0.5) = -ln(2)
        var oneOp = new GpuFloatOperand(sig1.Handle, 0, false);
        res = arith.Log(ref execContext, in oneOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.True(resSig.IsZero());

        var halfOp = new GpuFloatOperand(sig1.Handle, -1, false);
        res = arith.Log(ref execContext, in halfOp, resHandle);
        Assert.True(res.IsNegative);
        Assert.Equal(Math.Log(0.5), ToDouble(resSig, res), precision: 6);

        // Multi-word GpuBigUInt.DivideByUInt32: (0x1_0000_0005) / 3
        using var multiWord = new GpuBigUInt(customCtx, new uint[] { 5u, 1u });
        var (quot, rem) = multiWord.DivideByUInt32(3u);
        using (quot)
        {
            var expected = ((BigInteger.One << 32) + 5) / 3;
            Assert.Equal(expected, quot.ToBigInteger());
            Assert.Equal(0u, rem);
        }
    }

    private static GpuBigIntExecutionContext CreateContext(GpuWorkspace workspace) =>
        new(workspace, workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), workspace.Stream);

    private static void AssertExactValue(BigInteger num, BigInteger den, GpuBigUInt sig, GpuFloatResult res)
    {
        var actual = sig.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(actual, res.Exponent, res.IsNegative, num, den),
            $"expected {num}/{den}, actual {BigFloatReference.Describe(actual, res.Exponent, res.IsNegative)}");
    }

    // An addition whose exponent gap exceeds one word is still exact while the result fits the result words.
    // (0xFFFFFFFF << 33) +- 1 has 65 bits, so a 4-word result holds it without rounding.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Add_WideExponentGap_IsExact(bool rightIsNegative)
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var context = CreateContext(workspace);
        var arith = GSharpKernelFloatProvider.Instance;

        using var leftSig = new GpuBigUInt(workspace, [0xFFFFFFFFu]);
        using var rightSig = new GpuBigUInt(workspace, [1u]);
        using var resSig = new GpuBigUInt(workspace, 4);
        var left = new GpuFloatOperand(leftSig.Handle, 33);
        var right = new GpuFloatOperand(rightSig.Handle, 0, rightIsNegative);

        var res = arith.Add(ref context, in left, in right, resSig.Handle);

        // mutant: GSharpKernelFloatProvider.cs:136 `Math.Max` -> `Math.Min`, `NumWords + ...` -> `NumWords - ...`,
        // `(expDiff + 31) / 32` -> `(expDiff - 31) / 32` (the aligned buffer becomes 2 words and the 65-bit sum is cut)
        var expected = (new BigInteger(0xFFFFFFFFu) << 33) + (rightIsNegative ? -1 : 1);
        AssertExactValue(expected, 1, resSig, res);
    }

    // Zero operands give an unsigned zero with exponent 0 and a cleared result; invalid operands throw.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ZeroAndSadPaths()
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var arith = GSharpKernelFloatProvider.Instance;

        using var zeroSig = new GpuBigUInt(workspace, [0u]);
        using var threeSig = new GpuBigUInt(workspace, [3u]);
        using var twoSig = new GpuBigUInt(workspace, [2u]);
        using var resSig = new GpuBigUInt(workspace, 4);

        // Normalize(-0 * 2^5) is +0 * 2^0.
        // mutant: GSharpKernelFloatProvider.cs:67 `return new GpuFloatResult(0, false);` removed / `false` -> `true`
        {
            var context = CreateContext(workspace);
            var negZero = new GpuFloatOperand(zeroSig.Handle, 5, true);
            var res = arith.Normalize(ref context, in negZero, resSig.Handle);
            Assert.Equal(new GpuFloatResult(0, false), res);
        }

        // 0 / 0 is a division by zero, not the early return for a zero dividend.
        // mutant: GSharpKernelFloatProvider.cs:203 `throw` removed
        Assert.Throws<DivideByZeroException>(() =>
        {
            var context = CreateContext(workspace);
            var zero = new GpuFloatOperand(zeroSig.Handle, 0);
            arith.Divide(ref context, in zero, in zero, resSig.Handle);
        });

        // 0 / -3 is +0 and clears every word of a result that held garbage.
        // mutant: GSharpKernelFloatProvider.cs:215 ClearMemoryAsync removed, L216 `false` -> `true`
        {
            using var dirty = new GpuBigUInt(workspace, [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu]);
            var context = CreateContext(workspace);
            var zero = new GpuFloatOperand(zeroSig.Handle, 0);
            var negThree = new GpuFloatOperand(threeSig.Handle, 0, true);
            var res = arith.Divide(ref context, in zero, in negThree, dirty.Handle);
            Assert.Equal(new GpuFloatResult(0, false), res);
            Assert.Equal(BigInteger.Zero, dirty.ToBigInteger());
        }

        // Sqrt(0) is +0 and clears the result.
        // mutant: GSharpKernelFloatProvider.cs:293 ClearMemoryAsync removed, L294 `false` -> `true`
        {
            using var dirty = new GpuBigUInt(workspace, [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu]);
            var context = CreateContext(workspace);
            var zero = new GpuFloatOperand(zeroSig.Handle, 0);
            var res = arith.Sqrt(ref context, in zero, dirty.Handle);
            Assert.Equal(new GpuFloatResult(0, false), res);
            Assert.Equal(BigInteger.Zero, dirty.ToBigInteger());
        }

        // ln 0 is undefined.
        // mutant: GpuBigFloatTranscendentals.cs:267 `throw` removed
        Assert.Throws<InvalidOperationException>(() =>
        {
            var context = CreateContext(workspace);
            var zero = new GpuFloatOperand(zeroSig.Handle, 0);
            arith.Log(ref context, in zero, resSig.Handle);
        });

        // (-2)^3: a negative base is not supported.
        // mutant: GSharpKernelFloatProvider.cs:413 `throw` removed (GpuBigFloatTranscendentals.Pow ignores the sign of the
        // base and would return 8)
        var powError = Assert.Throws<InvalidOperationException>(() =>
        {
            var context = CreateContext(workspace);
            var negTwo = new GpuFloatOperand(twoSig.Handle, 0, true);
            var three = new GpuFloatOperand(threeSig.Handle, 0);
            arith.Pow(ref context, in negTwo, in three, resSig.Handle);
        });
        Assert.Contains("Pow", powError.Message, StringComparison.Ordinal);
    }

    // With a 4-word result, Exp and Log are accurate to a few ulps: relative error at most 2^-124
    // (128-bit result truncated by Normalize: 2^-127, plus the series truncation of the 192-bit fraction).
    // Reference: BigInteger fixed point with 400 fraction bits (BigFloatReference).
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    // mutant: GpuBigFloatTranscendentals.cs ExpSeriesPatersonStockmeyer lower blocks `(uint)(first + i)` ->
    // `(uint)(first + i + 1)` (wrong divisors in Smith's order)
    [InlineData(1)]
    // e^60 = 2^85 · e^r: k·ln 2 with ln 2 to W - s + bits(k) + 2 fraction bits.
    // mutant: GpuBigFloatTranscendentals.cs ReduceArgument `GetLn2(plan.Ln2FractionBits)` ->
    // `GetLn2(plan.Ln2FractionBits - 64)` (|k|·|L - ln 2| about 2^-121)
    [InlineData(60)]
    // e^-1 = 2^-3 · e^r with r = 3 ln 2 - 1, no reciprocal.
    // mutant: GpuBigFloatTranscendentals.cs ReduceArgument `GpuBigIntMath.Sub(kLn2, x, reduced)` ->
    // `GpuBigIntMath.Add(kLn2, x, reduced)` (r for +1 instead of -1)
    [InlineData(-1)]
    public void Exp_HighPrecision(int x)
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var context = CreateContext(workspace);
        var arith = GSharpKernelFloatProvider.Instance;

        using var xSig = new GpuBigUInt(workspace, [(uint)Math.Abs(x)]);
        using var resSig = new GpuBigUInt(workspace, 4);
        var operand = new GpuFloatOperand(xSig.Handle, 0, x < 0);

        var res = arith.Exp(ref context, in operand, resSig.Handle);

        Assert.False(res.IsNegative);
        var actual = BigFloatReference.ToFixed(resSig.ToBigInteger(), res.Exponent);
        var expected = BigFloatReference.ExpFixed(x);
        Assert.True(
            BigFloatReference.IsWithinRelative(actual, expected, 124),
            $"exp({x}): actual {BigFloatReference.Describe(resSig.ToBigInteger(), res.Exponent, false)}");
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Log_HighPrecision()
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var context = CreateContext(workspace);
        var arith = GSharpKernelFloatProvider.Instance;

        using var twoSig = new GpuBigUInt(workspace, [2u]);
        using var resSig = new GpuBigUInt(workspace, 4);
        var two = new GpuFloatOperand(twoSig.Handle, 0);

        var res = arith.Log(ref context, in two, resSig.Handle);

        // ln 2 to a relative error of 2^-124 (same bound as Exp_HighPrecision).
        // mutant: GpuBigFloatTranscendentals.cs MultiplyLn2 `GetLn2(ln2FractionBits)` -> `GetLn2(ln2FractionBits - 96)`
        // (ln 2 to 2^-96; 64 bits less still passes, the working precision has 36 bits to spare)
        Assert.False(res.IsNegative);
        var actual = BigFloatReference.ToFixed(resSig.ToBigInteger(), res.Exponent);
        Assert.True(
            BigFloatReference.IsWithinRelative(actual, BigFloatReference.Ln2Fixed(), 124),
            $"ln 2: actual {BigFloatReference.Describe(resSig.ToBigInteger(), res.Exponent, false)}");
    }

    // A square root or inverse square root that is exactly representable comes out exact. sqrt(9) = 3 came out
    // 3 - 2^-126 while Sqrt was a * rsqrt(a) truncated (#661).
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    // mutant: GSharpKernelFloatProvider.cs:367 `- 32L * (words + marginWords)` -> `... + 1` (4 and 16, inverse)
    // mutant: GpuBigIntDivRem.Sqrt.cs:102 `SqrtFloorCorrection(...)` removed (9: the estimate 3 - 2^-126 is kept)
    [InlineData(4u, false, 2, 1)]
    [InlineData(9u, false, 3, 1)]
    [InlineData(4u, true, 1, 2)]
    [InlineData(16u, true, 1, 4)]
    public void Sqrt_Rsqrt_PerfectSquares_AreExact(uint value, bool inverse, int num, int den)
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var context = CreateContext(workspace);
        var arith = GSharpKernelFloatProvider.Instance;

        using var sig = new GpuBigUInt(workspace, [value]);
        using var resSig = new GpuBigUInt(workspace, 4);
        var operand = new GpuFloatOperand(sig.Handle, 0);

        var res = inverse
            ? arith.Rsqrt(ref context, in operand, resSig.Handle)
            : arith.Sqrt(ref context, in operand, resSig.Handle);

        AssertExactValue(num, den, resSig, res);
    }

    // An irrational square root of a 1-word operand is still computed to the 4-word result precision:
    // relative error at most 2^-124 (truncation to 128 bits plus the Newton and product truncations).
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    // mutant: GSharpKernelFloatProvider.cs:360 `n - words / 2` -> `n - words` (2 and 3, inverse: 113 bits)
    [InlineData(2u, false)]
    [InlineData(2u, true)]
    [InlineData(3u, false)]
    [InlineData(3u, true)]
    public void Sqrt_Rsqrt_HighPrecision(uint value, bool inverse)
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var context = CreateContext(workspace);
        var arith = GSharpKernelFloatProvider.Instance;

        using var sig = new GpuBigUInt(workspace, [value]);
        using var resSig = new GpuBigUInt(workspace, 4);
        var operand = new GpuFloatOperand(sig.Handle, 0);

        var res = inverse
            ? arith.Rsqrt(ref context, in operand, resSig.Handle)
            : arith.Sqrt(ref context, in operand, resSig.Handle);

        Assert.False(res.IsNegative);
        var actual = BigFloatReference.ToFixed(resSig.ToBigInteger(), res.Exponent);
        var expected = inverse ? BigFloatReference.InverseSqrtFixed(value) : BigFloatReference.SqrtFixed(value);
        Assert.True(
            BigFloatReference.IsWithinRelative(actual, expected, 124),
            $"{(inverse ? "rsqrt" : "sqrt")}({value}): actual {BigFloatReference.Describe(resSig.ToBigInteger(), res.Exponent, false)}");
    }

    // exp(0) is exactly 1, whatever the result buffer held before.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Exp_Zero_IntoDirtyBuffer_IsExactlyOne()
    {
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var context = CreateContext(workspace);
        var arith = GSharpKernelFloatProvider.Instance;

        using var zeroSig = new GpuBigUInt(workspace, [0u]);
        using var dirty = new GpuBigUInt(workspace, [0x12345678u, 0x9ABCDEF0u, 0x0F0F0F0Fu, 0x00000001u]);
        var zero = new GpuFloatOperand(zeroSig.Handle, 0);

        // mutant: GpuBigFloatTranscendentals.cs WriteOne `ClearMemoryAsync` removed (the low words keep the old value)
        var res = arith.Exp(ref context, in zero, dirty.Handle);
        AssertExactValue(1, 1, dirty, res);
    }
}
