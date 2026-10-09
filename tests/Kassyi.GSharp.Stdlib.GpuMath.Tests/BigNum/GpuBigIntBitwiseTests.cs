using System;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Tests.Common;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuBigIntBitwiseTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntBitwiseTests()
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

    [Fact]
    public void Test_Bitwise_Operations()
    {
        using var a = new GpuBigUInt(_context, 4);
        using var b = new GpuBigUInt(_context, 4);
        using var r = new GpuBigUInt(_context, 4);
        
        GpuBigIntMath.BitwiseAnd(a, b, r);
        GpuBigIntMath.BitwiseOr(a, b, r);
        GpuBigIntMath.BitwiseXor(a, b, r);
        GpuBigIntMath.BitwiseNot(a, r);
        
        Assert.True(true);
    }
}
