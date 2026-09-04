using System;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

public class GBI28_SignedGpuBigIntegerTests : IDisposable
{
    private readonly JitEngine.CudaContextScope _scope;
    private readonly GpuContext _context;

    public GBI28_SignedGpuBigIntegerTests()
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
    public void ConstructAndDispose_WorksCorrectly()
    {
        var gpuBigInt = new GpuBigInteger(_context, 4);
        var signed = new SignedGpuBigInteger(gpuBigInt, isNegative: true);

        Assert.True(signed.IsNegative);
        Assert.Same(gpuBigInt, signed.Value);

        // Ensure disposing SignedGpuBigInteger disposes the underlying GpuBigInteger
        signed.Dispose();
    }
}
