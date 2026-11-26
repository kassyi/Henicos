
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class SignedExpressionsTests : IDisposable
{
    private readonly GpuTestWorkspace _testWorkspace;

    public SignedExpressionsTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _testWorkspace = new GpuTestWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    private GpuWorkspace Context => _testWorkspace.Workspace;

    public void Dispose() => _testWorkspace.Dispose();

    private static GpuBigInt Create(GpuWorkspace context, BigInteger val, int numWords)
    {
        bool isNeg = val < 0;
        if (isNeg) val = -val;
        
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));
        
        var u = context.RentTemp(numWords);
        var hostData = new uint[numWords];
        buffer.CopyTo(hostData, 0);
        context.Provider.CopyHostToDevice(u.DevicePointer, hostData, numWords, context.Stream);
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
        
        

        var gpuA = Create(Context, a, 1);
        var gpuB = Create(Context, b, 1);

        var expr = gpuA + gpuB;
        using var gpuRes = expr.Evaluate(Context);
        
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
        
        

        var gpuA = Create(Context, a, 1);
        var gpuB = Create(Context, b, 1);

        var expr = gpuA - gpuB;
        using var gpuRes = expr.Evaluate(Context);
        
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
        
        

        var gpuA = Create(Context, a, 1);
        var gpuB = Create(Context, b, 1);

        var expr = gpuA * gpuB;
        using var gpuRes = expr.Evaluate(Context);
        
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
        
        

        var gpuA = Create(Context, a, 1);
        var gpuB = Create(Context, b, 1);

        var expr = gpuA / gpuB;
        using var gpuRes = expr.Evaluate(Context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a / b), res);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(0)]
    public void Negation_Tests(long a)
    {
        
        

        var gpuA = Create(Context, a, 1);

        var expr = -((GpuBigIntExpr)gpuA);
        using var gpuRes = expr.Evaluate(Context);
        
        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(-a), res);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(0)]
    public void BitwiseNot_Tests(long a)
    {
        
        

        var gpuA = Create(Context, a, 1);

        var expr = ~((GpuBigIntExpr)gpuA);
        using var gpuRes = expr.Evaluate(Context);

        // ~x == -x - 1, as BigInteger, independent of the word count
        Assert.Equal(~new BigInteger(a), ToBigInt(gpuRes));
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(10, -5)]
    [InlineData(-10, 5)]
    [InlineData(-10, -5)]
    [InlineData(20, 7)]
    [InlineData(-20, 7)]
    [InlineData(20, -7)]
    [InlineData(-20, -7)]
    [InlineData(3, 10)]
    [InlineData(-3, 10)]
    public void Modulo_Tests(long a, long b)
    {
        var gpuA = Create(Context, a, 1);
        var gpuB = Create(Context, b, 1);

        var expr = gpuA % gpuB;
        using var gpuRes = expr.Evaluate(Context);

        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a % b), res);
    }
}
