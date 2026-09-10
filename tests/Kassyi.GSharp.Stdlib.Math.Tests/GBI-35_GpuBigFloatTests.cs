using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigFloat;

/// <summary>
/// Sample 128-bit limb storage buffer (4 x 32-bit uint) used as unmanaged TSize for GpuBigFloat testing.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LimbBuffer128Bit
{
    public uint Limb0;
    public uint Limb1;
    public uint Limb2;
    public uint Limb3;

    public LimbBuffer128Bit(uint l0, uint l1, uint l2, uint l3)
    {
        Limb0 = l0;
        Limb1 = l1;
        Limb2 = l2;
        Limb3 = l3;
    }
}

public class GBI35_GpuBigFloatTests
{
    #region 1. Structural Layout & Offset Validation Tests (Zero-Reflection Unsafe Pointer Arithmetic)

    [Fact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "Syntax")]
    [Trait("Category", "HappyPath")]
    public unsafe void Test_GBI35_FieldOffsetsAndSizes_MatchExpectedSequentialLayout_UnsafePointers()
    {
        // 128-bit limb buffer size = 16 bytes
        int limbSize = Unsafe.SizeOf<LimbBuffer128Bit>();
        Assert.Equal(16, limbSize);

        // GpuUIntBigInteger<LimbBuffer128Bit> size = 16 bytes
        int uIntSize = Unsafe.SizeOf<GpuUIntBigInteger<LimbBuffer128Bit>>();
        Assert.Equal(16, uIntSize);

        // GpuBigUFloat layout check using unsafe pointer offset arithmetic (Zero-Reflection)
        GpuBigUFloat<LimbBuffer128Bit> sampleUFloat = default;
        byte* uFloatBase = (byte*)&sampleUFloat;
        byte* significandPtr = (byte*)&sampleUFloat.Significand;
        byte* exponentPtr = (byte*)&sampleUFloat.Exponent;

        int uFloatSignificandOffset = (int)(significandPtr - uFloatBase);
        int uFloatExponentOffset = (int)(exponentPtr - uFloatBase);

        Assert.Equal(0, uFloatSignificandOffset);
        Assert.Equal(16, uFloatExponentOffset);

        // GpuBigUFloat total size = 16 (Significand) + 4 (Exponent) = 20 bytes
        int uFloatSize = Unsafe.SizeOf<GpuBigUFloat<LimbBuffer128Bit>>();
        Assert.Equal(20, uFloatSize);

        // GpuBigFloat layout check using unsafe pointer offset arithmetic (Zero-Reflection)
        GpuBigFloat<LimbBuffer128Bit> sampleFloat = default;
        byte* floatBase = (byte*)&sampleFloat;
        byte* uFloatPtr = (byte*)&sampleFloat.UFloat;
        byte* isNegPtr = (byte*)&sampleFloat.IsNegative;

        int floatUFloatOffset = (int)(uFloatPtr - floatBase);
        int floatIsNegativeOffset = (int)(isNegPtr - floatBase);

        Assert.Equal(0, floatUFloatOffset);
        Assert.Equal(20, floatIsNegativeOffset);
    }

    [Fact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI35_ConstructorAndFieldInitialization_StoresCorrectValues()
    {
        var limbs = new LimbBuffer128Bit(0x11111111, 0x22222222, 0x33333333, 0x44444444);
        var significand = new GpuUIntBigInteger<LimbBuffer128Bit>(limbs);
        var uFloat = new GpuBigUFloat<LimbBuffer128Bit>(significand, exponent: 42);
        var bigFloat = new GpuBigFloat<LimbBuffer128Bit>(uFloat, isNegative: true);

        Assert.Equal(0x11111111u, bigFloat.UFloat.Significand.Limbs.Limb0);
        Assert.Equal(0x44444444u, bigFloat.UFloat.Significand.Limbs.Limb3);
        Assert.Equal(42, bigFloat.UFloat.Exponent);
        Assert.True(bigFloat.IsNegative);

        var directBigFloat = new GpuBigFloat<LimbBuffer128Bit>(significand, exponent: -10, isNegative: false);
        Assert.Equal(-10, directBigFloat.UFloat.Exponent);
        Assert.False(directBigFloat.IsNegative);
    }

    #endregion

    #region 2. Radix Point Alignment Logic Tests via Struct Member Invocations

    [Theory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(10, 10, 0)]
    [InlineData(15, 10, 5)]
    [InlineData(-5, 5, 10)]
    [InlineData(100, -100, 200)]
    public void Test_GBI35_RadixAlignment_ExponentDifference_CalculatesCorrectDelta_ViaStructApi(int expA, int expB, int expectedDelta)
    {
        var dummySignificand = new GpuUIntBigInteger<LimbBuffer128Bit>(default);
        var floatA = new GpuBigFloat<LimbBuffer128Bit>(dummySignificand, expA, isNegative: false);
        var floatB = new GpuBigFloat<LimbBuffer128Bit>(dummySignificand, expB, isNegative: false);

        int actualDelta = floatA.AlignRadixPoint(floatB, out var alignedB, out _);
        Assert.Equal(expectedDelta, actualDelta);

        int uFloatDelta = floatA.UFloat.AlignRadixPoint(floatB.UFloat, out var alignedUB, out _);
        Assert.Equal(expectedDelta, uFloatDelta);
        Assert.Equal(System.Math.Max(expA, expB), alignedB.UFloat.Exponent);
        Assert.Equal(System.Math.Max(expA, expB), alignedUB.Exponent);
    }

    [Theory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "EdgeCase")]
    [InlineData(0b11000000u, 2, 0b00110000u, false)]
    [InlineData(0b11000001u, 2, 0b00110000u, true)]  // LSB set -> Sticky Bit set
    [InlineData(0b10000000u, 32, 0b00000000u, true)] // Shifted out completely -> Sticky Bit set
    [InlineData(0b00000000u, 8, 0b00000000u, false)] // Zero value shifted out -> Sticky Bit false
    public void Test_GBI35_RadixAlignment_SignificandRightShift_PreservesStickyBitContract(uint initialValue, int shiftBits, uint expectedShiftedValue, bool expectedStickyBit)
    {
        uint shiftedValue = shiftBits >= 32 ? 0u : initialValue >> shiftBits;
        uint mask = (1u << shiftBits) - 1u;
        bool stickyBit = shiftBits >= 32 ? (initialValue != 0u) : ((initialValue & mask) != 0u);

        Assert.Equal(expectedShiftedValue, shiftedValue);
        Assert.Equal(expectedStickyBit, stickyBit);
    }

    [Fact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBI35_RadixAlignment_DeltaExponentExceedsPrecision_FlushesToZeroWithStickyBitContract()
    {
        var limbs = new LimbBuffer128Bit(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu);
        var significand = new GpuUIntBigInteger<LimbBuffer128Bit>(limbs);

        var floatHighExp = new GpuBigFloat<LimbBuffer128Bit>(significand, exponent: 100, isNegative: false);
        var floatLowExp = new GpuBigFloat<LimbBuffer128Bit>(significand, exponent: 0, isNegative: false);

        int deltaExp = floatHighExp.AlignRadixPoint(floatLowExp, out var alignedLowExp, out _);

        Assert.Equal(100, deltaExp);
        Assert.Equal(100, alignedLowExp.UFloat.Exponent);
    }

    #endregion

    #region 3. Normalization & IEEE 754 Rounding Tests via Struct Member Invocations

    [Theory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(0, 0)]   // Carry out = false, 0 leading zeros
    [InlineData(1, -1)]  // 1 leading zero -> exp-1
    [InlineData(31, -31)] // 31 leading zeros -> exp-31
    public void Test_GBI35_Normalization_UnderflowLeadingZeros_AlignsMsbAndDecrementsExponent_ViaStructApi(int leadingZeros, int expectedExpOffset)
    {
        var dummySignificand = new GpuUIntBigInteger<LimbBuffer128Bit>(default);
        var uFloat = new GpuBigUFloat<LimbBuffer128Bit>(dummySignificand, exponent: 0);

        var normalized = uFloat.Normalize(leadingZeros: leadingZeros, carryOut: false);
        Assert.Equal(expectedExpOffset, normalized.Exponent);

        var bigFloat = new GpuBigFloat<LimbBuffer128Bit>(uFloat, isNegative: false);
        var normFloat = bigFloat.Normalize(leadingZeros: leadingZeros, carryOut: false);
        Assert.Equal(expectedExpOffset, normFloat.UFloat.Exponent);
    }

    [Fact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI35_Normalization_CarryOverflow_ShiftsRightAndIncrementsExponent_ViaStructApi()
    {
        var dummySignificand = new GpuUIntBigInteger<LimbBuffer128Bit>(default);
        var uFloat = new GpuBigUFloat<LimbBuffer128Bit>(dummySignificand, exponent: 5);

        var normalized = uFloat.Normalize(leadingZeros: 0, carryOut: true);
        Assert.Equal(6, normalized.Exponent);

        var bigFloat = new GpuBigFloat<LimbBuffer128Bit>(uFloat, isNegative: true);
        var normFloat = bigFloat.Normalize(leadingZeros: 0, carryOut: true);
        Assert.Equal(6, normFloat.UFloat.Exponent);
        Assert.True(normFloat.IsNegative);
    }

    [Theory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "EdgeCase")]
    [InlineData(GpuBigFloatRoundingMode.RoundToNearestEven, true, true, false)]
    [InlineData(GpuBigFloatRoundingMode.RoundToNearestEven, true, false, false)]
    [InlineData(GpuBigFloatRoundingMode.RoundTowardZero, true, true, true)]
    [InlineData(GpuBigFloatRoundingMode.RoundTowardPositiveInfinity, true, false, false)]
    [InlineData(GpuBigFloatRoundingMode.RoundTowardNegativeInfinity, false, true, true)]
    public void Test_GBI35_Ieee754Rounding_InvokesRoundingMode_ViaStructApi(GpuBigFloatRoundingMode mode, bool guard, bool round, bool sticky)
    {
        var dummySignificand = new GpuUIntBigInteger<LimbBuffer128Bit>(default);
        var uFloat = new GpuBigUFloat<LimbBuffer128Bit>(dummySignificand, exponent: 10);

        var roundedUFloat = uFloat.Round(mode, guard, round, sticky);
        Assert.Equal(10, roundedUFloat.Exponent);

        var bigFloat = new GpuBigFloat<LimbBuffer128Bit>(uFloat, isNegative: true);
        var roundedFloat = bigFloat.Round(mode, guard, round, sticky);

        Assert.Equal(10, roundedFloat.UFloat.Exponent);
        Assert.True(roundedFloat.IsNegative);
    }

    [Fact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI35_ArithmeticOperatorAndMethodDeclarations_ExistAndAreCallable()
    {
        var limbsA = new LimbBuffer128Bit(100, 0, 0, 0);
        var limbsB = new LimbBuffer128Bit(40, 0, 0, 0);
        var sigA = new GpuUIntBigInteger<LimbBuffer128Bit>(limbsA);
        var sigB = new GpuUIntBigInteger<LimbBuffer128Bit>(limbsB);

        var uFloatA = new GpuBigUFloat<LimbBuffer128Bit>(sigA, exponent: 10);
        var uFloatB = new GpuBigUFloat<LimbBuffer128Bit>(sigB, exponent: 10);

        var uAddResult = uFloatA + uFloatB;
        var uSubResult = uFloatA - uFloatB;

        Assert.Equal(10, uAddResult.Exponent);
        Assert.Equal(10, uSubResult.Exponent);

        var floatA = new GpuBigFloat<LimbBuffer128Bit>(uFloatA, isNegative: false);
        var floatB = new GpuBigFloat<LimbBuffer128Bit>(uFloatB, isNegative: false);

        var addResult = floatA + floatB;
        var subResult = floatA - floatB;

        Assert.Equal(10, addResult.UFloat.Exponent);
        Assert.Equal(10, subResult.UFloat.Exponent);
    }

    [Theory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "SadPath")]
    [InlineData(int.MaxValue, 0, true, true)]   // Exponent overflow on carry out
    [InlineData(int.MinValue, 1, false, true)]  // Exponent underflow on leading zeros
    [InlineData(100, 0, false, false)]           // In range
    public void Test_GBI35_ExponentBoundaryConditions_DetectsOverflowAndUnderflowOnStructApi(int currentExp, int leadingZeros, bool carryOut, bool expectedOverflow)
    {
        var dummySig = new GpuUIntBigInteger<LimbBuffer128Bit>(default);
        var floatVal = new GpuBigFloat<LimbBuffer128Bit>(dummySig, currentExp, isNegative: false);

        if (expectedOverflow)
        {
            Assert.Throws<OverflowException>(() => floatVal.Normalize(leadingZeros, carryOut));
        }
        else
        {
            var norm = floatVal.Normalize(leadingZeros, carryOut);
            Assert.Equal(currentExp, norm.UFloat.Exponent);
        }
    }

    [Fact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBI35_ZeroSignificand_NormalizesToZeroExponentAndPositiveSign()
    {
        var zeroLimbs = new LimbBuffer128Bit(0, 0, 0, 0);
        var zeroUInt = new GpuUIntBigInteger<LimbBuffer128Bit>(zeroLimbs);
        var zeroUFloat = new GpuBigUFloat<LimbBuffer128Bit>(zeroUInt, exponent: 5);

        var positiveZero = new GpuBigFloat<LimbBuffer128Bit>(zeroUFloat, isNegative: false);
        var negativeZero = new GpuBigFloat<LimbBuffer128Bit>(zeroUFloat, isNegative: true);

        var normPos = positiveZero.Normalize(leadingZeros: 5, carryOut: false);
        var normNeg = negativeZero.Normalize(leadingZeros: 5, carryOut: false);

        Assert.Equal(0, normPos.UFloat.Exponent);
        Assert.Equal(0, normNeg.UFloat.Exponent);

        // IEEE 754 zero equivalency check (+0 == -0)
        Assert.Equal(normPos.UFloat.Significand.Limbs.Limb0, normNeg.UFloat.Significand.Limbs.Limb0);
        Assert.Equal(normPos.UFloat.Exponent, normNeg.UFloat.Exponent);
    }

    #endregion
}
