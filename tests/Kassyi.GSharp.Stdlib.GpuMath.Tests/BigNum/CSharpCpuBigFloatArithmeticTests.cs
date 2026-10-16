
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

    private static double ToDouble(GpuBigUInt sig, int exp, bool sign)
    {
        var val = (double)sig.ToBigInteger() * Math.Pow(2.0, exp);
        return sign ? -val : val;
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

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var bHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(bSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        // Add: 4 + 2 = 6
        arith.Add(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(6.0, ToDouble(resSig, resExp, resSign), precision: 10);

        // Multiply: 4 * 2 = 8
        arith.Multiply(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(8.0, ToDouble(resSig, resExp, resSign), precision: 10);

        // Divide: 4 / 2 = 2
        arith.Divide(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(2.0, ToDouble(resSig, resExp, resSign), precision: 10);
    }

    [SkippableFact]
    public void Test_Arithmetic_Reciprocal_Sqrt_Rsqrt()
    {
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var resSig = new GpuBigUInt(_context, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        // Reciprocal: 1 / 4 = 0.25
        arith.Reciprocal(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(0.25, ToDouble(resSig, resExp, resSign), precision: 10);

        // Sqrt: sqrt(4) = 2.0
        arith.Sqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(2.0, ToDouble(resSig, resExp, resSign), precision: 10);

        // Rsqrt: 1 / sqrt(4) = 0.5
        arith.Rsqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(0.5, ToDouble(resSig, resExp, resSign), precision: 10);
    }

    [SkippableFact]
    public void Test_Arithmetic_Exp_Log_Pow()
    {
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 2 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 3 });
        using var resSig = new GpuBigUInt(_context, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var bHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(bSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        // Exp: exp(2)
        arith.Exp(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(Math.Exp(2.0), ToDouble(resSig, resExp, resSign), precision: 8);

        // Log: ln(2)
        arith.Log(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(Math.Log(2.0), ToDouble(resSig, resExp, resSign), precision: 8);

        // Pow: 2^3 = 8
        arith.Pow(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
        Assert.Equal(8.0, ToDouble(resSig, resExp, resSign), precision: 6);
    }

    [SkippableFact]
    public void Test_Arithmetic_Normalize_DivideByUInt32()
    {
        var execContext = CreateExecContext();
        var arith = CSharpCpuBigFloatArithmetic.Instance;

        using var aSig = new GpuBigUInt(_context, new uint[] { 10 });
        using var resSig = new GpuBigUInt(_context, 4);

        var aHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(aSig.DevicePointer, 1), 0, 1);
        var resHandle = new GpuBigIntMemoryHandle(new GpuBigIntDeviceBuffer(resSig.DevicePointer, 4), 0, 4);

        arith.Normalize(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);
        Assert.Equal(10.0, ToDouble(resSig, resExp, resSign), precision: 10);

        arith.DivideByUInt32(ref execContext, aHandle, 0, false, 3, resHandle, out resExp, out resSign, out uint rem);
        Assert.False(resSign);
        Assert.Equal(0, resExp);
        Assert.Equal(3UL, (ulong)resSig.ToBigInteger());
        Assert.Equal(1u, rem);
    }
}
