using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn31_38_AdvancedMemoryTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-14")]
    public void TestFixedBufferKernel()
    {
        uint[] data = [5, 0];
        Syn31_38_AdvancedMemoryKernelsWrapper.Launch_FixedBufferKernel(data);
        Assert.Equal(15u, data[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-36")]
    public void TestPointerIncrementKernel()
    {
        uint[] src = [10, 20, 30];
        uint[] dest = [0, 0, 0];
        Syn31_38_AdvancedMemoryKernelsWrapper.Launch_PointerIncrementKernel(src, dest);
        Assert.Equal(10u, dest[0]);
        Assert.Equal(20u, dest[1]);
        Assert.Equal(30u, dest[2]);
    }

    [Fact(Skip = "未実装: SYN-31 (アンマネージド関数ポインタ delegate*)")]
    [Trait("FeatureId", "SYN-31")]
    [Trait("Category", "Syntax")]
    public void Test_SYN31_FunctionPointers_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-32 (MemoryMarshal.Cast<TFrom, TTo>)")]
    [Trait("FeatureId", "SYN-32")]
    [Trait("Category", "Syntax")]
    public void Test_SYN32_MemoryMarshalCast_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-33 (ref フィールド / ref struct メンバ)")]
    [Trait("FeatureId", "SYN-33")]
    [Trait("Category", "Syntax")]
    public void Test_SYN33_RefFields_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-34 (ハードウェア SIMD ベクトル Vector128<T>)")]
    [Trait("FeatureId", "SYN-34")]
    [Trait("Category", "Syntax")]
    public void Test_SYN34_SimdVector128_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-37 (Unsafe クラスの低レベルポインタ演算)")]
    [Trait("FeatureId", "SYN-37")]
    [Trait("Category", "Syntax")]
    public void Test_SYN37_UnsafePointerArithmetic_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-38 (固定長バッファ fixed uint buffer[64])")]
    [Trait("FeatureId", "SYN-38")]
    [Trait("Category", "Syntax")]
    public void Test_SYN38_FixedSizeBuffers_Transpilation()
    {
    }
}

