using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class AdvancedMemorySyntaxTests
{
    [Fact(Skip = "未実装: SYN-31 (アンマネージド関数ポインタ delegate*)")]
    [Trait("RoadmapId", "SYN-31")]
    [Trait("Category", "Syntax")]
    public void Test_SYN31_FunctionPointers_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-32 (MemoryMarshal.Cast<TFrom, TTo>)")]
    [Trait("RoadmapId", "SYN-32")]
    [Trait("Category", "Syntax")]
    public void Test_SYN32_MemoryMarshalCast_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-33 (ref フィールド / ref struct メンバ)")]
    [Trait("RoadmapId", "SYN-33")]
    [Trait("Category", "Syntax")]
    public void Test_SYN33_RefFields_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-34 (ハードウェア SIMD ベクトル Vector128<T>)")]
    [Trait("RoadmapId", "SYN-34")]
    [Trait("Category", "Syntax")]
    public void Test_SYN34_SimdVector128_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-37 (Unsafe クラスの低レベルポインタ演算)")]
    [Trait("RoadmapId", "SYN-37")]
    [Trait("Category", "Syntax")]
    public void Test_SYN37_UnsafePointerArithmetic_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-38 (固定長バッファ fixed uint buffer[64])")]
    [Trait("RoadmapId", "SYN-38")]
    [Trait("Category", "Syntax")]
    public void Test_SYN38_FixedSizeBuffers_Transpilation()
    {
    }
}
