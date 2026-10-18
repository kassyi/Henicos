
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigIntMatrix2x2Tests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntMatrix2x2Tests()
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

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Identity_And_Multiply_And_Apply()
    {
        CudaCapability.EnsureGpuAvailable();

        using var id = GpuBigIntMatrix2x2.Identity(_context, 4);
        Assert.False(id.IsOddStep);

        using var m1 = new GpuBigIntMatrix2x2(_context, 4, false);
        using var t1 = new GpuBigUInt(_context, [1u]);
        using var t2 = new GpuBigUInt(_context, [2u]);
        m1.M00.CopyFrom(t2);
        m1.M01.CopyFrom(t1);
        m1.M10.CopyFrom(t1);
        m1.M11.CopyFrom(t2);

        using var id2 = GpuBigIntMatrix2x2.Identity(_context, 4);
        using var result = GpuBigIntMatrix2x2.Multiply(m1, id2);

        Assert.Equal(2u, result.M00.ToHostArray()[0]);
        Assert.Equal(1u, result.M01.ToHostArray()[0]);
        Assert.Equal(1u, result.M10.ToHostArray()[0]);
        Assert.Equal(2u, result.M11.ToHostArray()[0]);

        using var a = new GpuBigUInt(_context, [2u]);
        using var b = new GpuBigUInt(_context, [2u]);
        using var outA = new GpuBigUInt(_context, 4);
        using var outB = new GpuBigUInt(_context, 4);
        result.Apply(a, b, outA, outB);
        Assert.Equal(2u, outA.ToHostArray()[0]); // 2*2 - 1*2 = 2
        Assert.Equal(2u, outB.ToHostArray()[0]); // 2*2 - 1*2 = 2
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Apply_WhenOperandsHaveDifferentWordLengths_AndIsOddStep_ComputesCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();

        using var m = new GpuBigIntMatrix2x2(_context, 2, isOddStep: true);
        using var t1 = new GpuBigUInt(_context, [1u]);
        using var t3 = new GpuBigUInt(_context, [3u]);
        m.M00.CopyFrom(t1);
        m.M01.CopyFrom(t3);
        m.M10.CopyFrom(t3);
        m.M11.CopyFrom(t1);

        // a has 1 word, b has 3 words (unequal NumWords)
        using var a = new GpuBigUInt(_context, [5u]);
        using var b = new GpuBigUInt(_context, [4u, 0u, 0u]);
        using var outA = new GpuBigUInt(_context, 4);
        using var outB = new GpuBigUInt(_context, 4);

        m.Apply(a, b, outA, outB);

        // IsOddStep == true:
        // A' = M01 * b - M00 * a = 3 * 4 - 1 * 5 = 7
        // B' = M10 * a - M11 * b = 3 * 5 - 1 * 4 = 11
        Assert.Equal(7u, outA.ToHostArray()[0]);
        Assert.Equal(11u, outB.ToHostArray()[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_And_Methods_ThrowExpectedExceptions_OnInvalidOrDisposedInputs()
    {
        CudaCapability.EnsureGpuAvailable();

        Assert.Throws<ArgumentNullException>(() => new GpuBigIntMatrix2x2(null!, 4, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuBigIntMatrix2x2(_context, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuBigIntMatrix2x2(_context, -1, false));

        var disposedMatrix = GpuBigIntMatrix2x2.Identity(_context, 2);
        disposedMatrix.Dispose();

        using var validMatrix = GpuBigIntMatrix2x2.Identity(_context, 2);
        Assert.Throws<ObjectDisposedException>(() => GpuBigIntMatrix2x2.Multiply(disposedMatrix, validMatrix));
        Assert.Throws<ObjectDisposedException>(() => GpuBigIntMatrix2x2.Multiply(validMatrix, disposedMatrix));

        using var a = new GpuBigUInt(_context, [1u]);
        using var b = new GpuBigUInt(_context, [1u]);
        using var outA = new GpuBigUInt(_context, 2);
        using var outB = new GpuBigUInt(_context, 2);

        Assert.Throws<ObjectDisposedException>(() => disposedMatrix.Apply(a, b, outA, outB));

        using var otherWorkspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var foreignA = new GpuBigUInt(otherWorkspace, [1u]);
        Assert.Throws<ArgumentException>(() => validMatrix.Apply(foreignA, b, outA, outB));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void HypergeometricTerms_Merge_MatchesCpuBinarySplitting()
    {
        CudaCapability.EnsureGpuAvailable();

        var cpuLeft = ChudnovskyAlgorithm.ComputeSubtree(0, 2);
        var cpuRight = ChudnovskyAlgorithm.ComputeSubtree(2, 4);
        var cpuExpected = ChudnovskyAlgorithm.ComputeSubtree(0, 4);

        using var left = CreateHypergeometricTerms(_context, cpuLeft);
        using var right = CreateHypergeometricTerms(_context, cpuRight);
        using var merged = HypergeometricTerms.Merge(_context, left, right);

        Assert.Equal(BigInteger.Abs(cpuExpected.P), merged.P.Value.ToBigInteger());
        Assert.Equal(cpuExpected.P < 0, merged.P.IsNegative);
        Assert.Equal(cpuExpected.Q, merged.Q.ToBigInteger());
        Assert.Equal(BigInteger.Abs(cpuExpected.T), merged.T.Value.ToBigInteger());
        Assert.Equal(cpuExpected.T < 0, merged.T.IsNegative);
    }

    private static HypergeometricTerms CreateHypergeometricTerms(
        GpuWorkspace workspace,
        ChudnovskyAlgorithm.ChudnovskyPqt pqt)
    {
        var p = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, BigInteger.Abs(pqt.P)), pqt.P < 0);
        var q = GpuBigUInt.FromBigInteger(workspace, pqt.Q);
        var t = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, BigInteger.Abs(pqt.T)), pqt.T < 0);
        return new HypergeometricTerms(p, q, t);
    }
}
