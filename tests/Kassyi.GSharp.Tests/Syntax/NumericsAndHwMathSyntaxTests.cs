using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class NumericsAndHwMathSyntaxTests
{
    [Fact(Skip = "未実装: SYN-64 (半精度浮動小数点数 Half / half2)")]
    [Trait("RoadmapId", "SYN-64")]
    [Trait("Category", "Syntax")]
    public void Test_SYN64_HalfPrecision_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-65 (ネイティブ整数 nint, nuint)")]
    [Trait("RoadmapId", "SYN-65")]
    [Trait("Category", "Syntax")]
    public void Test_SYN65_NativeInt_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-66 (128ビット整数 UInt128, Int128)")]
    [Trait("RoadmapId", "SYN-66")]
    [Trait("Category", "Syntax")]
    public void Test_SYN66_Int128UInt128_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-67 (BFloat16 / FP8 型 BFloat16, fp8)")]
    [Trait("RoadmapId", "SYN-67")]
    [Trait("Category", "Syntax")]
    public void Test_SYN67_BFloat16Fp8_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-69 (64ビット飽和 / キャリー演算)")]
    [Trait("RoadmapId", "SYN-69")]
    [Trait("Category", "Syntax")]
    public void Test_SYN69_AddWithCarry64_Transpilation()
    {
    }
}
