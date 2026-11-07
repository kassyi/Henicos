
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class SignedExpressionsTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public SignedExpressionsTests()
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

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(-5, 1)]
    [InlineData(10, 1)]
    [InlineData(-10, 1)]
    [InlineData(-1, 1)]
    [InlineData(4294967294, 1)]
    [InlineData(-4294967295, 1)]
    [InlineData(5, 3)]
    [InlineData(-5, 3)]
    [InlineData(4294967295, 2)]
    [InlineData(-4294967296, 2)]
    public void BitwiseNot_IsMinusXMinusOne_AsBigInteger(long a, int words)
    {
        // ~x == -x - 1 as for BigInteger and C# int, the same value for every word count (it inverted the magnitude and
        // kept the sign: ~(+5) in 1 word gave +4294967290). ~(-1) is +0, not -0, so the sign is checked too.
        // mutant: SignedBitwiseNotExpression non-negative branch `new GpuBigInt(resultBuffer, true)` -> `false`
        // mutant: SignedBitwiseNotExpression `GpuBigIntMath.Sub(magnitude, 1u, ...)` result sign `false` -> `true`
        var gpuA = Create(_context, a, words);

        using var gpuRes = (~(GpuBigIntExpr)gpuA).Evaluate(_context);

        var expected = ~new BigInteger(a);
        Assert.Equal((expected, expected < 0), (ToBigInt(gpuRes), gpuRes.IsNegative));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void BitwiseNot_NegativeZero_IsMinusOne()
    {
        // A negative zero (left by a product or a negation of zero) has the value 0, so ~(-0) is -1 like ~0, not
        // |0| - 1, which would wrap to +4294967295.
        // mutant: SignedBitwiseNotExpression without the `magnitude.IsZero()` branch
        var negativeZero = new GpuBigInt(Create(_context, 0, 1).Value, true);

        using var gpuRes = (~(GpuBigIntExpr)negativeZero).Evaluate(_context);

        Assert.Equal((BigInteger.MinusOne, true), (ToBigInt(gpuRes), gpuRes.IsNegative));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(1025)]
    public void BitwiseNot_AllOnesMagnitude_Overflows(int words)
    {
        // ~(2^(32 n) - 1) is -2^(32 n), which needs another word: the width of the operand is kept, so the next read
        // throws instead of returning -0.
        // mutant: CheckedScalarAddKernel `if (carry != 0)` -> `if (carry == 0)`
        // mutant: DeviceScalarAddSubParallelWithCarry `idx == numWords - 1` -> `idx == numWords - 2`
        var gpuA = Create(_context, (BigInteger.One << (32 * words)) - 1, words);

        using var gpuRes = (~(GpuBigIntExpr)gpuA).Evaluate(_context);

        var exception = Assert.Throws<GpuOverflowException>(() => gpuRes.Value.ToBigInteger());
        Assert.Equal(("~", "~x0"), (exception.Origins[0].Operation, exception.Origins[0].Subexpression));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void BitwiseNot_InCapturedGraph_NonNegativeRuns_NegativeIsNotSupported()
    {
        // ~x of x >= 0 adds no host read, so it runs in a CUDA graph; for x < 0 the sign of the result depends on whether
        // |x| is zero, which a graph cannot read, so the capture fails explicitly instead of with a CUDA error.
        // mutant: SignedBitwiseNotExpression without the IsCapturing check (CudaException from the capture)
        var positive = Create(_context, 5, 1);
        using (var graph = (~(GpuBigIntExpr)positive).CompileToGraph(_context))
        {
            graph.Execute();
            Assert.Equal((new BigInteger(6), true), (graph.Result.Value.ToBigInteger(), graph.Result.IsNegative));
        }

        var negative = Create(_context, -5, 1);
        var expression = ~(GpuBigIntExpr)negative;
        Assert.Throws<NotSupportedException>(() => expression.CompileToGraph(_context));
        using var direct = expression.Evaluate(_context);
        Assert.Equal(new BigInteger(4), ToBigInt(direct));
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
        var gpuA = Create(_context, a, 1);
        var gpuB = Create(_context, b, 1);

        var expr = gpuA % gpuB;
        using var gpuRes = expr.Evaluate(_context);

        var res = ToBigInt(gpuRes);
        Assert.Equal(new BigInteger(a % b), res);
    }
}
