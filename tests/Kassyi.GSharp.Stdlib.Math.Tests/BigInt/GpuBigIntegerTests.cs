
namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

[Trait("Category", "GpuExecution")]
public class GpuBigIntegerTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntegerTests()
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
    [Trait("Category", "HappyPath")]
    public void Test_ConstructAndDispose_WorksCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        var gpuBigInt = new GpuUIntBigInteger(_context, 4);
        var signed = new GpuBigInteger(gpuBigInt, isNegative: true);

        Assert.True(signed.IsNegative);
        Assert.Equal(gpuBigInt, signed.Value);

        // Ensure disposing GpuBigInteger disposes the underlying GpuUIntBigInteger
        signed.Dispose();
    }
}
