
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class UnsignedExpressionsTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public UnsignedExpressionsTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }
    private static GpuBigUInt Create(GpuWorkspace _context, BigInteger val, int numWords)
    {
        val = BigInteger.Abs(val);
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));
        
        var hostData = new uint[numWords];
        buffer.CopyTo(hostData, 0);
        return new GpuBigUInt(_context, hostData);
    }

    private static BigInteger ToBigInt(GpuBigUInt gpuVal)
    {
        return gpuVal.ToBigInteger();
    }

    [Theory]
    [InlineData(10, 5)]
    public void Addition_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA + gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a + b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, 10)]
    public void Subtraction_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA - gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a - b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(0, 5)]
    public void Multiplication_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA * gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a * b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(20, 7)]
    public void Division_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA / gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a / b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(20, 7)]
    public void Modulo_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA % gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a % b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(0xFF, 0x0F)]
    public void BitwiseAnd_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA & gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a & b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(0xF0, 0x0F)]
    public void BitwiseOr_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA | gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a | b), res);
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(0xFF, 0x0F)]
    public void BitwiseXor_Tests(long a, long b)
    {
        
        

        using var gpuA = Create(_context, a, 1);
        using var gpuB = Create(_context, b, 1);

        var expr = gpuA ^ gpuB;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a ^ b), res);
    }

    [Theory]
    [InlineData(10, 2)]
    public void ShiftLeft_Tests(long a, int shift)
    {
        
        

        using var gpuA = Create(_context, a, 1);

        var expr = gpuA << shift;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a << shift), res);
    }

    [Theory]
    [InlineData(10, 2)]
    public void ShiftRight_Tests(long a, int shift)
    {
        
        

        using var gpuA = Create(_context, a, 1);

        var expr = gpuA >> shift;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a >> shift), res);
    }
    [Theory]
    [InlineData(10)]
    [InlineData(0xFF)]
    public void BitwiseNot_Tests(long a)
    {
        using var gpuA = Create(_context, a, 1);
        var expr = ~gpuA;
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        var expected = new BigInteger(~(uint)a); // since it's 1 word (uint)
        Assert.Equal(expected, res);
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(25, 5)]
    [InlineData(0, 0)]
    public void Sqrt_Tests(long a, long expectedSqrt)
    {
        using var gpuA = Create(_context, a, 1);
        var expr = GpuBigUIntExpr.Sqrt(gpuA);
        using var gpuRes = expr.Node.Evaluate(_context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(expectedSqrt), res);
    }
}
