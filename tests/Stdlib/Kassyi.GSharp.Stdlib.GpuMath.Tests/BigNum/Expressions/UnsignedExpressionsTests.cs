
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

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Add")]
    [InlineData("Mul")]
    [InlineData("Not")]
    [InlineData("ShiftLeft")]
    [InlineData("Leaf")]
    public void Evaluate_TargetNarrowerThanTheExpression_ThrowsAndLeavesTheWordsAfterIt(string root)
    {
        // A 1-word target at the start of a guard buffer: the 4-word expression (8 words for Mul) is rejected before
        // anything is written, as the signed nodes reject a target of another width, and the guard words keep their value.
        // mutant: GpuBigUIntExpressionExtensions.Evaluate `targetResult.NumWords < expr.NumWords` check removed (Add, Mul,
        // Not, ShiftLeft write 4 or 8 words through the 1-word target; Leaf truncates)
        // mutant: the check `<` -> `<=` (every equal-width target in this class throws)
        const uint Guard = 0xDEAD_BEEFu;
        using var a = Create(_context, 1, 4);
        using var b = Create(_context, 2, 4);
        using var guardBuffer = new GpuBigUInt(_context, Enumerable.Repeat(Guard, 9).ToArray());
        GpuBigUIntView whole = guardBuffer;
        var target = whole.Slice(0, 1).AsBorrowedBigUInt();
        GpuBigUIntExpr ea = a;
        GpuBigUIntExpr eb = b;
        var expression = root switch
        {
            "Add" => ea + eb,
            "Mul" => ea * eb,
            "Not" => ~ea,
            "ShiftLeft" => ea << 3,
            "Leaf" => ea,
            _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unknown root."),
        };

        var error = Assert.Throws<ArgumentException>(() => expression.Node.Evaluate(_context, target));

        Assert.Equal("targetResult", error.ParamName);
        Assert.Equal(Enumerable.Repeat(Guard, 9).ToArray(), guardBuffer.ToHostArray());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Evaluate_TargetOfTheExpressionWidth_HoldsTheResult()
    {
        // The same width as the expression is accepted (the check rejects only a narrower target).
        using var a = Create(_context, 1, 4);
        using var b = Create(_context, 2, 4);
        using var target = new GpuBigUInt(_context, 4);

        ((GpuBigUIntExpr)a + b).Node.Evaluate(_context, target);

        Assert.Equal(new BigInteger(3), target.ToBigInteger());
    }
}
