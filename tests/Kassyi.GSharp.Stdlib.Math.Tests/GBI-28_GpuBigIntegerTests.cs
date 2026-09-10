
namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

[Trait("Category", "GpuExecution")]
public class GBI28_GpuBigIntegerTests : IDisposable
{
    private readonly JitEngine.CudaContextScope _scope;
    private readonly GpuContext _context;

    public GBI28_GpuBigIntegerTests()
    {
        _scope = new JitEngine.CudaContextScope();
        _context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [Fact]
    [Trait("FeatureId", "GBI-28")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI28_ConstructAndDispose_WorksCorrectly()
    {
        var gpuBigInt = new GpuUIntBigInteger(_context, 4);
        var signed = new GpuBigInteger(gpuBigInt, isNegative: true);

        Assert.True(signed.IsNegative);
        Assert.Same(gpuBigInt, signed.Value);

        // Ensure disposing GpuBigInteger disposes the underlying GpuUIntBigInteger
        signed.Dispose();
    }
}
