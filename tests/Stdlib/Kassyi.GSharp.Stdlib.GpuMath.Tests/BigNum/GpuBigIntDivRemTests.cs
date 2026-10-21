
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
}
