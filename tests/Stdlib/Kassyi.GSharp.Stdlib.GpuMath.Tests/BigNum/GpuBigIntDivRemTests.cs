
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuBigIntDivRemTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GpuBigIntDivRemTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_Reduce_InverseSqrt_Heron()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(customCtx, new uint[] { 4000000000, 4000000000, 4000000000, 4000000000 });
        using var b = new GpuBigUInt(customCtx, new uint[] { 2, 0, 0, 0 });
        using var q = new GpuBigUInt(customCtx, 4);
        using var r = new GpuBigUInt(customCtx, 4);

        // Divide
        GpuBigIntDivRem.DivRem(a, b, q, r);

        // Reciprocal
        using var recip = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.Reciprocal(a, recip, 128);

        // InverseSqrt
        using var invSqrt = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.InverseSqrt(a, invSqrt, 0);

        // Sqrt
        using var sqrt = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.Sqrt(a, sqrt);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_MegaDivision_4096Words_MatchesCpuReference()
    {
        const int NumWords = 4096; // 131,072 bits
        using var workspace = new GpuWorkspace(NumWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(42);
        var leftData = new uint[NumWords];
        var rightData = new uint[NumWords];
        for (var i = 0; i < NumWords; i++)
        {
            leftData[i] = (uint)rnd.Next();
            rightData[i] = (uint)rnd.Next();
        }

        leftData[^1] |= 0x80000000u;
        rightData[^1] |= 0x80000000u;
        // Make dividend larger than divisor at the top word so quotient is non-zero
        leftData[^1] |= 0xFFFF0000u;
        rightData[^1] &= 0x8000FFFFu;

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuB = new GpuBigUInt(workspace, rightData);
        using var gpuQ = new GpuBigUInt(workspace, NumWords);
        using var gpuR = new GpuBigUInt(workspace, NumWords);

        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);

        var expectedA = gpuA.ToBigInteger();
        var expectedB = gpuB.ToBigInteger();
        var expectedQ = BigInteger.DivRem(expectedA, expectedB, out var expectedR);

        Assert.Equal(expectedQ, gpuQ.ToBigInteger());
        Assert.Equal(expectedR, gpuR.ToBigInteger());

        using var gpuSqrt = new GpuBigUInt(workspace, NumWords);
        GpuBigIntDivRem.Sqrt(gpuA, gpuSqrt);
        Assert.Equal(expectedA.Sqrt(), gpuSqrt.ToBigInteger());

        // Also test GpuBigFloat.Divide scenario (shiftedLeft has 3 * NumWords = 12,288 words, rightOperand has 4,096 words)
        var execCtx = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager, workspace.Stream);
        var floatProvider = GSharpKernelFloatProvider.Instance;
        using var floatResult = new GpuBigUInt(workspace, NumWords);
        var leftOp = new GpuFloatOperand(gpuA.Handle, 1000);
        var rightOp = new GpuFloatOperand(gpuB.Handle, 1000);
        var floatRes = floatProvider.Divide(ref execCtx, in leftOp, in rightOp, floatResult.Handle);

        var cpuFloatProvider = CSharpCpuBigFloatArithmetic.Instance;
        using var cpuResult = new GpuBigUInt(workspace, NumWords);
        var expectedFloatRes = cpuFloatProvider.Divide(ref execCtx, in leftOp, in rightOp, cpuResult.Handle);

        Assert.Equal(expectedFloatRes.Exponent, floatRes.Exponent);
        Assert.Equal(expectedFloatRes.IsNegative, floatRes.IsNegative);
        Assert.Equal(cpuResult.ToBigInteger(), floatResult.ToBigInteger());
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2048, 1024)]
    [InlineData(4096, 2048)]
    [InlineData(4096, 4095)]
    public void Test_DivRem_MegaDivision_ShiftedDivisorBoundary_MatchesCpuReference(int numWords, int divisorActiveWords)
    {
        using var workspace = new GpuWorkspace(numWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(1337 + numWords + divisorActiveWords);
        var leftData = new uint[numWords];
        var rightData = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            leftData[i] = (uint)rnd.Next();
        }

        for (var i = 0; i < divisorActiveWords; i++)
        {
            rightData[i] = (uint)rnd.Next();
        }

        leftData[^1] |= 0x80000000u;
        rightData[divisorActiveWords - 1] |= 0x00010000u;

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuB = new GpuBigUInt(workspace, rightData);
        using var gpuQ = new GpuBigUInt(workspace, numWords);
        using var gpuR = new GpuBigUInt(workspace, numWords);

        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);

        var expectedA = gpuA.ToBigInteger();
        var expectedB = gpuB.ToBigInteger();
        var expectedQ = BigInteger.DivRem(expectedA, expectedB, out var expectedR);

        Assert.Equal(expectedQ, gpuQ.ToBigInteger());
        Assert.Equal(expectedR, gpuR.ToBigInteger());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Test_DivRem_MegaDivision_4096Words_DivideByZero_ThrowsDivideByZeroException()
    {
        const int NumWords = 4096;
        using var workspace = new GpuWorkspace(NumWords * 2, GpuExecutionEngine.GSharpAot);

        var leftData = new uint[NumWords];
        leftData[^1] = 0x80000000u;

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuB = new GpuBigUInt(workspace, NumWords);
        using var gpuQ = new GpuBigUInt(workspace, NumWords);
        using var gpuR = new GpuBigUInt(workspace, NumWords);

        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR));
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(65)]
    [InlineData(256)]
    [InlineData(600)]
    [InlineData(1024)]
    public void DivRem_SmallAndBoundaryDivisors_MatchesBigIntegerReference(int numWords)
    {
        using var workspace = new GpuWorkspace(numWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(20261025 + numWords);
        var leftData = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            leftData[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        leftData[^1] |= 0x80000000u;

        var twoWordDivisor = new uint[numWords];
        twoWordDivisor[0] = (uint)rnd.NextInt64(0, 1L << 32);
        twoWordDivisor[1] = (uint)rnd.NextInt64(0, 1L << 32) | 0x80000000u;

        var halfWordCount = numWords / 2;
        var halfWordDivisor = new uint[numWords];
        for (var i = 0; i < halfWordCount; i++)
        {
            halfWordDivisor[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        halfWordDivisor[halfWordCount - 1] |= 0x80000000u;

        var divisors = new BigInteger[]
        {
            BigInteger.One,
            new(2),
            new(3),
            BigInteger.One << 32,
            (BigInteger.One << 33) + BigInteger.One,
            BigInteger.One << 15,
            BigInteger.One << 31,
            BigInteger.One << 47,
            BigIntExtensions.ToBigInteger(twoWordDivisor),
            BigIntExtensions.ToBigInteger(halfWordDivisor)
        };

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuQ = new GpuBigUInt(workspace, numWords);
        using var gpuR = new GpuBigUInt(workspace, numWords);
        var expectedA = gpuA.ToBigInteger();

        foreach (var divisor in divisors)
        {
            using var gpuB = GpuBigUInt.FromBigInteger(workspace, divisor, minWords: numWords);
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);

            var expectedQ = BigInteger.DivRem(expectedA, divisor, out var expectedR);
            Assert.Equal(expectedQ, gpuQ.ToBigInteger());
            Assert.Equal(expectedR, gpuR.ToBigInteger());
        }
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(600)]
    public void Gcd_BoundaryOperandPairs_MatchesBigIntegerReference(int numWords)
    {
        using var workspace = new GpuWorkspace(numWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(20261025 + numWords);
        var arrA = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            arrA[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        arrA[^1] |= 0x40000000u;
        var bigA = BigIntExtensions.ToBigInteger(arrA);

        var pairs = new (BigInteger Left, BigInteger Right)[]
        {
            (bigA, BigInteger.One),
            (BigInteger.One, bigA),
            (bigA, bigA + BigInteger.One),
            (bigA, bigA)
        };

        using var gpuResult = new GpuBigUInt(workspace, numWords);
        foreach (var (left, right) in pairs)
        {
            using var gpuLeft = GpuBigUInt.FromBigInteger(workspace, left, minWords: numWords);
            using var gpuRight = GpuBigUInt.FromBigInteger(workspace, right, minWords: numWords);

            GpuBigIntMath.Gcd(gpuLeft, gpuRight, gpuResult);

            var expected = BigInteger.GreatestCommonDivisor(left, right);
            Assert.Equal(expected, gpuResult.ToBigInteger());
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Gcd_WithOne64Words_CompletesViaFastPath()
    {
        const int NumWords = 64;
        using var workspace = new GpuWorkspace(NumWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(20261025);
        var arrA = new uint[NumWords];
        for (var i = 0; i < NumWords; i++)
        {
            arrA[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        arrA[^1] |= 0x80000000u;
        var bigA = BigIntExtensions.ToBigInteger(arrA);

        using var gpuA = GpuBigUInt.FromBigInteger(workspace, bigA, minWords: NumWords);
        using var gpuOne = GpuBigUInt.FromBigInteger(workspace, BigInteger.One, minWords: NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        GpuBigIntMath.Gcd(gpuA, gpuOne, gpuResult);

        Assert.Equal(BigInteger.One, gpuResult.ToBigInteger());
    }
}
