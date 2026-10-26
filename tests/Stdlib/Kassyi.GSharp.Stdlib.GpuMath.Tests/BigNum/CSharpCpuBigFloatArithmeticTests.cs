
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class CSharpCpuBigFloatArithmeticTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public CSharpCpuBigFloatArithmeticTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    private GpuBigIntExecutionContext CreateExecContext()
    {
        return new GpuBigIntExecutionContext(_context, _context.MemoryManager, new LegacyProviderArithmeticAdapter(), _context.Stream);
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
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 2 });
        using var resSig = new GpuBigUInt(_context, 4);

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
        Assert.Equal(2.0, ToDouble(resSig, res), precision: 10);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    public void Test_Arithmetic_Reciprocal_Sqrt_Rsqrt()
    {
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var resSig = new GpuBigUInt(_context, 4);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var resHandle = resSig.Handle;

        // Reciprocal: 1 / 4 = 0.25
        var res = arith.Reciprocal(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(0.25, ToDouble(resSig, res), precision: 10);

        // Sqrt: sqrt(4) = 2.0
        res = arith.Sqrt(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(2.0, ToDouble(resSig, res), precision: 10);

        // Rsqrt: 1 / sqrt(4) = 0.5
        res = arith.Rsqrt(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(0.5, ToDouble(resSig, res), precision: 10);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    public void Test_Arithmetic_Exp_Log_Pow()
    {
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 2 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 3 });
        using var resSig = new GpuBigUInt(_context, 4);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var bOp = new GpuFloatOperand(bSig.Handle, 0, false);
        var resHandle = resSig.Handle;

        // Exp: exp(2)
        var res = arith.Exp(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(Math.Exp(2.0), ToDouble(resSig, res), precision: 8);

        // Log: ln(2)
        res = arith.Log(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(Math.Log(2.0), ToDouble(resSig, res), precision: 8);

        // Pow: 2^3 = 8
        res = arith.Pow(ref execContext, in aOp, in bOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(8.0, ToDouble(resSig, res), precision: 6);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    public void Test_Arithmetic_Normalize_DivideByUInt32()
    {
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 10 });
        using var resSig = new GpuBigUInt(_context, 4);

        var aOp = new GpuFloatOperand(aSig.Handle, 0, false);
        var resHandle = resSig.Handle;

        var res = arith.Normalize(ref execContext, in aOp, resHandle);
        Assert.False(res.IsNegative);
        Assert.Equal(10.0, ToDouble(resSig, res), precision: 10);

        var divRes = arith.DivideByUInt32(ref execContext, in aOp, 3, resHandle);
        Assert.False(divRes.IsNegative);
        Assert.Equal(0, divRes.Exponent);
        Assert.Equal(3UL, (ulong)resSig.ToBigInteger());
        Assert.Equal(1u, divRes.Remainder);
    }
}
