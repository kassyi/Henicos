using System.Numerics;
using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn64_69_NumericsAndHwMathTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-35")]
    [InlineData(0u, 0ul)]
    [InlineData(1u, 1ul)]
    [InlineData(uint.MaxValue, ulong.MaxValue)]
    [InlineData(0x80000000u, 0x8000000000000000ul)]
    [InlineData(0x55555555u, 0x5555555555555555ul)]
    [InlineData(0xAAAAAAAAu, 0xAAAAAAAAAAAAAAAAul)]
    [InlineData(0b00000000_10101010_11110000_00001000u, 0b00000000_00000000_10101010_11110000_00001000_00000000_00000000_00001000ul)]
    public void TestBitOperationsKernel_DataDriven(uint u32Input, ulong u64Input)
    {
        uint[] u32Data = new uint[7];
        u32Data[0] = u32Input;

        ulong[] u64Data = new ulong[7];
        u64Data[0] = u64Input;

        Syn64_69_NumericsAndHwMathKernelsWrapper.Launch_BitOperationsTestKernel(u32Data, u64Data);

        uint u32Val = u32Data[0];
        Assert.Equal((uint)BitOperations.LeadingZeroCount(u32Val), u32Data[1]);
        Assert.Equal((uint)BitOperations.PopCount(u32Val), u32Data[2]);
        Assert.Equal((uint)BitOperations.TrailingZeroCount(u32Val), u32Data[3]);
        Assert.Equal((uint)BitOperations.Log2(u32Val), u32Data[4]);
        Assert.Equal(BitOperations.RotateLeft(u32Val, 5), u32Data[5]);
        Assert.Equal(BitOperations.RotateRight(u32Val, 3), u32Data[6]);

        ulong u64Val = u64Data[0];
        Assert.Equal((ulong)BitOperations.LeadingZeroCount(u64Val), u64Data[1]);
        Assert.Equal((ulong)BitOperations.PopCount(u64Val), u64Data[2]);
        Assert.Equal((ulong)BitOperations.TrailingZeroCount(u64Val), u64Data[3]);
        Assert.Equal((ulong)BitOperations.Log2(u64Val), u64Data[4]);
        Assert.Equal(BitOperations.RotateLeft(u64Val, 10), u64Data[5]);
        Assert.Equal(BitOperations.RotateRight(u64Val, 7), u64Data[6]);
    }

    [Fact(Skip = "未実装: SYN-64 (半精度浮動小数点数 Half / half2)")]
    [Trait("FeatureId", "SYN-64")]
    [Trait("Category", "Syntax")]
    public void Test_SYN64_HalfPrecision_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-65 (ネイティブ整数 nint, nuint)")]
    [Trait("FeatureId", "SYN-65")]
    [Trait("Category", "Syntax")]
    public void Test_SYN65_NativeInt_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-66 (128ビット整数 UInt128, Int128)")]
    [Trait("FeatureId", "SYN-66")]
    [Trait("Category", "Syntax")]
    public void Test_SYN66_Int128UInt128_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-67 (BFloat16 / FP8 型 BFloat16, fp8)")]
    [Trait("FeatureId", "SYN-67")]
    [Trait("Category", "Syntax")]
    public void Test_SYN67_BFloat16Fp8_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-69 (64ビット飽和 / キャリー演算)")]
    [Trait("FeatureId", "SYN-69")]
    [Trait("Category", "Syntax")]
    public void Test_SYN69_AddWithCarry64_Transpilation()
    {
    }
}

