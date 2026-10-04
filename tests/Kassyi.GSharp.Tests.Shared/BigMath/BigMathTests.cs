using System.Runtime.CompilerServices;
using Xunit;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt;
using Kassyi.GSharp.Stdlib.Math.GpuBigFloat;
using Kassyi.GSharp.Tests.Shared.Helpers;

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

    [SkippableFact]
    public void TestGpuBigFloat_Initialization()
    {
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var uInt = new GpuUIntBigInteger(context, 4); // 4 words
        var uFloat = new GpuBigUFloat(uInt, 0);
        var bigFloat = new GpuBigFloat(uFloat, false);

        Assert.NotNull(bigFloat);
        Assert.Equal(0, bigFloat.UFloat.Exponent);
        Assert.False(bigFloat.IsNegative);
    }

    [SkippableFact]
    public void TestGpuBigIntMatrix2x2Host()
    {
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        using var mat = GpuBigIntMatrix2x2.Identity(context, 4);

        Assert.NotNull(mat);
        Assert.Equal(4, mat.M00.NumWords);
    }

    [SkippableFact]
    public void TestGpuBigIntMatrix2x2_SadPath_InvalidWordCount()
    {
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        Assert.ThrowsAny<Exception>(() => GpuBigIntMatrix2x2.Identity(context, 0));
        Assert.ThrowsAny<Exception>(() => GpuBigIntMatrix2x2.Identity(context, -1));
    }

    [Fact]
    public void TestGpuUIntBigInteger_SadPath_NullContext()
    {
        Assert.Throws<ArgumentNullException>(() => new GpuUIntBigInteger(null!, 4));
    }
}

