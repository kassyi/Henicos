using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_31_38_AdvancedMemoryTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-14")]
    public void TestFixedBufferKernel()
    {
        uint[] dataArray = [5, 0];
        SYN_31_38_AdvancedMemoryKernelsWrapper.Launch_FixedBufferKernel(dataArray);

        Assert.Equal(15u, dataArray[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-36")]
    public void TestPointerIncrementKernel()
    {
        uint[] src = [10, 20, 30];
        uint[] dest = [0, 0, 0];

        SYN_31_38_AdvancedMemoryKernelsWrapper.Launch_PointerIncrementKernel(src, dest);

        Assert.Equal(10u, dest[0]);
        Assert.Equal(20u, dest[1]);
        Assert.Equal(30u, dest[2]);
    }

    [Fact(Skip = "未実装: SYN-31 (アンマネージド関数ポインタ delegate*)")]
    [Trait("FeatureId", "SYN-31")]
    [Trait("Category", "Syntax")]
    public void Test_SYN31_UnsafeAsPointer_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-32 (MemoryMarshal.Cast<TFrom, TTo> による型昇格・再解釈)")]
    [Trait("FeatureId", "SYN-32")]
    [Trait("Category", "Syntax")]
    public void Test_SYN32_MemoryMarshalCast_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-33 (ref フィールド ref struct かつ ref int Field C# 11)")]
    [Trait("FeatureId", "SYN-33")]
    [Trait("Category", "Syntax")]
    public void Test_SYN33_RefFields_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-34 (Vector128<T> / Vector256<T> SIMD 型)")]
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

    [Fact(Skip = "未実装: SYN-38 (fixed int Buffer[32] 固定サイズバッファフィールド)")]
    [Trait("FeatureId", "SYN-38")]
    [Trait("Category", "Syntax")]
    public void Test_SYN38_FixedSizeBuffers_Transpilation()
    {
    }
}
