using Kassyi.GSharp.Runtime;
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

        var expr = (a + b) * c;

        // Verify that compile-time AST expression tree is properly composed as fused binary nodes
        Assert.IsType<MulExpression>(expr.Node);
        var mulNode = (MulExpression)expr.Node;
        Assert.IsType<AddExpression>(mulNode.Left);
        Assert.Equal(c, (GpuUIntBigInteger)mulNode.Right);

        result.Value = expr;

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

        var expr = (a + b) * (c - d);

        // Verify fused AST composition for compound expression
        Assert.IsType<MulExpression>(expr.Node);
        var mulNode = (MulExpression)expr.Node;
        Assert.IsType<AddExpression>(mulNode.Left);
        Assert.IsType<SubExpression>(mulNode.Right);

        result.Value = expr;

        var expected = BigInteger.Parse("450000"); // (500 + 250) * (1000 - 400) = 750 * 600
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-06")]
    [Trait("Category", "EdgeCases")]
    [InlineData(0u, 0u, 10u, 0u)]
    [InlineData(1u, 1u, 1u, 2u)]
    [InlineData(uint.MaxValue / 2, uint.MaxValue / 4, 2u, 0u)]
    [InlineData(uint.MaxValue, 0u, 1u, 0u)]
    [InlineData(0x80000000u, 0x80000000u, 2u, 0u)]
    [InlineData(0x55555555u, 0xAAAAAAAAu, 3u, 0u)]
    [InlineData(uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u)]
    public void Test_GBI06_Fusion_BoundaryValues(uint valA, uint valB, uint valC, uint dummy)
    {
        CudaCapability.EnsureGpuAvailable();
        using var a = new GpuUIntBigInteger(_context, new uint[] { valA, 0, 0, 0 });
        using var b = new GpuUIntBigInteger(_context, new uint[] { valB, 0, 0, 0 });
        using var c = new GpuUIntBigInteger(_context, new uint[] { valC, 0, 0, 0 });
        using var result = new GpuUIntBigInteger(_context, 8);

        var expr = (a + b) * c;
        Assert.IsType<MulExpression>(expr.Node);

        result.Value = expr;

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
