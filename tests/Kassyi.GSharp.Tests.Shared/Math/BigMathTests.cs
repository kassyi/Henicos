using System.Runtime.CompilerServices;
using Xunit;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.BigMath;

[Trait("Category", "GpuExecution")]
public class BigMathTests : TranspilerTestBase
{
    [SkippableTheory]
    [InlineData(0u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void TestGpuUInt128Host(uint value)
    {
        var a = new GpuUInt128(value);
        Assert.Equal(16, Unsafe.SizeOf<LimbBuffer4>());
        Assert.Equal(16, Unsafe.SizeOf<GpuUInt128>());
    }

    [SkippableTheory]
    [InlineData(0u)]
    [InlineData(123u)]
    [InlineData(uint.MaxValue)]
    public void TestGpuUInt256Host(uint value)
    {
        var a = new GpuUInt256(value);
        Assert.Equal(32, Unsafe.SizeOf<LimbBuffer8>());
        Assert.Equal(32, Unsafe.SizeOf<GpuUInt256>());
    }

    [SkippableTheory]
    [InlineData(0u)]
    [InlineData(456u)]
    [InlineData(uint.MaxValue)]
    public void TestGpuUInt512Host(uint value)
    {
        var a = new GpuUInt512(value);
        Assert.Equal(64, Unsafe.SizeOf<LimbBuffer16>());
        Assert.Equal(64, Unsafe.SizeOf<GpuUInt512>());
    }

    [SkippableTheory]
    [InlineData(0u)]
    [InlineData(99u)]
    [InlineData(uint.MaxValue)]
    public void TestGpuFloat128Host(uint value)
    {
        var u128 = new GpuUInt128(value);
        var f128 = new GpuFloat128(u128, 0, false);
        Assert.Equal(16, Unsafe.SizeOf<LimbBuffer4>());
        Assert.Equal(24, Unsafe.SizeOf<GpuFloat128>());
    }

    [SkippableTheory]
    [InlineData(0u)]
    [InlineData(199u)]
    [InlineData(uint.MaxValue)]
    public void TestGpuFloat256Host(uint value)
    {
        var u256 = new GpuUInt256(value);
        var f256 = new GpuFloat256(u256, 0, false);
        Assert.Equal(32, Unsafe.SizeOf<LimbBuffer8>());
        Assert.Equal(40, Unsafe.SizeOf<GpuFloat256>());
    }

    [SkippableTheory]
    [InlineData(0u)]
    [InlineData(299u)]
    [InlineData(uint.MaxValue)]
    public void TestGpuFloat512Host(uint value)
    {
        var u512 = new GpuUInt512(value);
        var f512 = new GpuFloat512(u512, 0, false);
        Assert.Equal(64, Unsafe.SizeOf<LimbBuffer16>());
        Assert.Equal(72, Unsafe.SizeOf<GpuFloat512>());
    }

    [SkippableFact]
    public void TestGpuBigIntMatrix2x2Host()
    {
        SkipIfCpuDialect();
        
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);
        using var mat = GpuBigIntMatrix2x2.Identity(context, 4);
        
        Assert.NotNull(mat);
        Assert.Equal(4, mat.M00.NumWords);
    }

    [SkippableFact]
    public void TestGpuBigIntMatrix2x2_SadPath_InvalidWordCount()
    {
        SkipIfCpuDialect();
        
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);
        Assert.ThrowsAny<Exception>(() => GpuBigIntMatrix2x2.Identity(context, 0));
        Assert.ThrowsAny<Exception>(() => GpuBigIntMatrix2x2.Identity(context, -1));
    }

    [Fact]
    public void TestGpuUIntBigInteger_SadPath_NullContext()
    {
        Assert.Throws<ArgumentNullException>(() => new GpuUIntBigInteger(null!, 4));
    }

    [Fact]
    public void TestGpuBigInteger_SadPath_NullValue()
    {
        Assert.Throws<ArgumentNullException>(() => new GpuBigInteger(null!, false));
    }
}
