using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

[Trait("Category", "GpuExecution")]
public class GBI06_KernelFusionTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuContext _context;

    public GBI06_KernelFusionTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-06")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI06_ComplexExpressionTreeFusion_AdditionAndMultiplication()
    {
        CudaCapability.EnsureGpuAvailable();
        // (a + b) * c
        using var a = new GpuUIntBigInteger(_context, new uint[] { 100, 0, 0, 0 });
        using var b = new GpuUIntBigInteger(_context, new uint[] { 200, 0, 0, 0 });
        using var c = new GpuUIntBigInteger(_context, new uint[] { 300, 0, 0, 0 });
        using var result = new GpuUIntBigInteger(_context, 8);

        result.Value = (a + b) * c;

        var expected = BigInteger.Parse("90000"); // (100 + 200) * 300
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-06")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI06_ChainedAddSubMulExpressionFusion()
    {
        CudaCapability.EnsureGpuAvailable();
        // (a + b) * (c - d)
        using var a = new GpuUIntBigInteger(_context, new uint[] { 500, 0, 0, 0 });
        using var b = new GpuUIntBigInteger(_context, new uint[] { 250, 0, 0, 0 });
        using var c = new GpuUIntBigInteger(_context, new uint[] { 1000, 0, 0, 0 });
        using var d = new GpuUIntBigInteger(_context, new uint[] { 400, 0, 0, 0 });
        using var result = new GpuUIntBigInteger(_context, 8);

        result.Value = (a + b) * (c - d);

        var expected = BigInteger.Parse("450000"); // (500 + 250) * (1000 - 400) = 750 * 600
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-06")]
    [Trait("Category", "EdgeCases")]
    [InlineData(0u, 0u, 10u, 0u)]
    [InlineData(1u, 1u, 1u, 2u)]
    [InlineData(uint.MaxValue / 2, uint.MaxValue / 4, 2u, 0u)]
    public void Test_GBI06_Fusion_BoundaryValues(uint valA, uint valB, uint valC, uint dummy)
    {
        CudaCapability.EnsureGpuAvailable();
        using var a = new GpuUIntBigInteger(_context, new uint[] { valA, 0, 0, 0 });
        using var b = new GpuUIntBigInteger(_context, new uint[] { valB, 0, 0, 0 });
        using var c = new GpuUIntBigInteger(_context, new uint[] { valC, 0, 0, 0 });
        using var result = new GpuUIntBigInteger(_context, 8);

        result.Value = (a + b) * c;

        var bigA = new BigInteger(valA);
        var bigB = new BigInteger(valB);
        var bigC = new BigInteger(valC);
        var expected = (bigA + bigB) * bigC;

        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    private static BigInteger ToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * 4 + 1];
        Buffer.BlockCopy(words, 0, bytes, 0, words.Length * 4);
        return new BigInteger(bytes);
    }
}
