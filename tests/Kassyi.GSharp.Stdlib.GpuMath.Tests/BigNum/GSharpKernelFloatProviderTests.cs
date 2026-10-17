
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
}
