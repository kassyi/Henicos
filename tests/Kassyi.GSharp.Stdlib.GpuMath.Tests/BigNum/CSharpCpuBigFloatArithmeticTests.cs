using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Tests.Common;
using Xunit;

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

        // Add
        arith.Add(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out int resExp, out bool resSign);
        Assert.False(resSign);

        // Multiply
        arith.Multiply(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);

        // Divide
        arith.Divide(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
        Assert.False(resSign);
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

        // Reciprocal
        arith.Reciprocal(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);

        // Sqrt
        arith.Sqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);

        // Rsqrt
        arith.Rsqrt(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);
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

        // Exp
        arith.Exp(ref execContext, aHandle, 0, false, resHandle, out int resExp, out bool resSign);

        // Log
        arith.Log(ref execContext, aHandle, 0, false, resHandle, out resExp, out resSign);

        // Pow
        arith.Pow(ref execContext, aHandle, 0, false, bHandle, 0, false, resHandle, out resExp, out resSign);
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
        
        arith.DivideByUInt32(ref execContext, aHandle, 0, false, 2, resHandle, out resExp, out resSign, out uint rem);
    }
}
