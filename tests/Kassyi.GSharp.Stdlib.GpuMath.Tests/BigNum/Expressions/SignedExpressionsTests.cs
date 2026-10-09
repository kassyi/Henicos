using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Kassyi.GSharp.Tests.Common;
using System.Numerics;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class SignedExpressionsTests : IDisposable
{
    private readonly Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public SignedExpressionsTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }
    private static GpuBigInt Create(GpuWorkspace _context, BigInteger val, int numWords)
    {
        bool isNeg = val < 0;
        if (isNeg) val = -val;
        
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));
        
        var u = _context.RentTemp(numWords);
        var hostData = new uint[numWords];
        buffer.CopyTo(hostData, 0);
        _context.Provider.CopyHostToDevice(u.DevicePointer, hostData, numWords, _context.Stream);
        return new GpuBigInt(u, isNeg);
    }

    private static BigInteger ToBigInt(GpuBigInt gpuVal)
    {
        var val = gpuVal.Value.ToBigInteger();
        return gpuVal.IsNegative ? -val : val;
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, -5)]
    [InlineData(-10, 5)]
    [InlineData(-10, -5)]
    [InlineData(5, 10)]
    [InlineData(-5, 10)]
    public void Addition_Tests(long a, long b)
    {
        
        

        var gpuA = Create(_context, a, 1);
        var gpuB = Create(_context, b, 1);

        var expr = gpuA + gpuB;
        using var gpuRes = expr.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a + b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, -5)]
    [InlineData(-10, 5)]
    [InlineData(-10, -5)]
    [InlineData(5, 10)]
    [InlineData(-5, 10)]
    [InlineData(0, 0)]
    public void Subtraction_Tests(long a, long b)
    {
        
        

        var gpuA = Create(_context, a, 1);
        var gpuB = Create(_context, b, 1);

        var expr = gpuA - gpuB;
        using var gpuRes = expr.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a - b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, -5)]
    [InlineData(-10, 5)]
    [InlineData(-10, -5)]
    public void Multiplication_Tests(long a, long b)
    {
        
        

        var gpuA = Create(_context, a, 1);
        var gpuB = Create(_context, b, 1);

        var expr = gpuA * gpuB;
        using var gpuRes = expr.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a * b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, -5)]
    [InlineData(-10, 5)]
    [InlineData(-10, -5)]
    [InlineData(20, 7)]
    [InlineData(-20, 7)]
    public void Division_Tests(long a, long b)
    {
        
        

        var gpuA = Create(_context, a, 1);
        var gpuB = Create(_context, b, 1);

        var expr = gpuA / gpuB;
        using var gpuRes = expr.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a / b), res);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(0)]
    public void Negation_Tests(long a)
    {
        
        

        var gpuA = Create(_context, a, 1);

        var expr = -((GpuBigIntExpr)gpuA);
        using var gpuRes = expr.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(-a), res);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(0)]
    public void BitwiseNot_Tests(long a)
    {
        
        

        var gpuA = Create(_context, a, 1);

        var expr = ~((GpuBigIntExpr)gpuA);
        using var gpuRes = expr.Evaluate(_context);
        
        // GPU Math BitwiseNot negates the underlying magnitude directly.
        // It does not change the sign flag.
        var targetBits = (~new BigInteger(Math.Abs(a))) & 0xFFFFFFFF;
        
        var res = ToBigInt(gpuRes);
        var resAbs = Math.Abs((long)res);
        Assert.Equal((long)targetBits, resAbs);
        Assert.Equal(a < 0, gpuRes.IsNegative);
    }
}
