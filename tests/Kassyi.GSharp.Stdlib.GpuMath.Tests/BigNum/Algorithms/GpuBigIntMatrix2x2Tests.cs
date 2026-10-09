
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
        using var t1 = new GpuBigUInt(_context, new uint[] { 1 });
        using var t2 = new GpuBigUInt(_context, new uint[] { 2 });
        using var t3 = new GpuBigUInt(_context, new uint[] { 3 });
        using var t4 = new GpuBigUInt(_context, new uint[] { 4 });
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
        
        using var a = new GpuBigUInt(_context, new uint[] { 2 });
        using var b = new GpuBigUInt(_context, new uint[] { 2 });
        using var outA = new GpuBigUInt(_context, 4);
        using var outB = new GpuBigUInt(_context, 4);
        result.Apply(a, b, outA, outB);
        Assert.Equal(2u, outA.ToHostArray()[0]); // 2*2 - 1*2 = 2
        Assert.Equal(2u, outB.ToHostArray()[0]); // 2*2 - 1*2 = 2
    }
}
