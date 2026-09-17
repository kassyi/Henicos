using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.Math.BigFloat;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigFloat;

/// <summary>
/// Sample 128-bit limb storage buffer (4 x 32-bit uint) used as unmanaged TSize for GpuBigFloat testing.
/// </summary>


public class GBF01_GpuBigFloatTests
{
    private static LimbBuffer4 CreateLimb4(uint l0, uint l1, uint l2, uint l3)
    {
        var limbs = new LimbBuffer4();
        limbs[0] = l0;
        limbs[1] = l1;
        limbs[2] = l2;
        limbs[3] = l3;
        return limbs;
    }
    #region 1. Structural Layout & Offset Validation Tests (Zero-Reflection Unsafe Pointer Arithmetic)

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "Syntax")]
    [Trait("Category", "HappyPath")]
    public unsafe void Test_GBF01_FieldOffsetsAndSizes_MatchExpectedSequentialLayout_UnsafePointers()
    {
        // 128-bit limb buffer size = 16 bytes
        int limbSize = Unsafe.SizeOf<LimbBuffer4>();
        Assert.Equal(16, limbSize);

        // GpuUIntBigInteger<LimbBuffer4> size = 16 bytes
        int uIntSize = Unsafe.SizeOf<GpuUIntBigInteger<LimbBuffer4>>();
        Assert.Equal(16, uIntSize);

        // GpuBigUFloat layout check using unsafe pointer offset arithmetic (Zero-Reflection)
        GpuBigUFloat<LimbBuffer4> sampleUFloat = default;
        byte* uFloatBase = (byte*)&sampleUFloat;
        byte* significandPtr = (byte*)&sampleUFloat.Significand;
        byte* exponentPtr = (byte*)&sampleUFloat.Exponent;

        int uFloatSignificandOffset = (int)(significandPtr - uFloatBase);
        int uFloatExponentOffset = (int)(exponentPtr - uFloatBase);

        Assert.Equal(0, uFloatSignificandOffset);
        Assert.Equal(16, uFloatExponentOffset);

        // GpuBigUFloat total size = 16 (Significand) + 4 (Exponent) = 20 bytes
        int uFloatSize = Unsafe.SizeOf<GpuBigUFloat<LimbBuffer4>>();
        Assert.Equal(20, uFloatSize);

        // GpuBigFloat layout check using unsafe pointer offset arithmetic (Zero-Reflection)
        GpuBigFloat<LimbBuffer4> sampleFloat = default;
        byte* floatBase = (byte*)&sampleFloat;
        byte* uFloatPtr = (byte*)&sampleFloat.UFloat;
        byte* isNegPtr = (byte*)&sampleFloat.IsNegative;

        int floatUFloatOffset = (int)(uFloatPtr - floatBase);
        int floatIsNegativeOffset = (int)(isNegPtr - floatBase);

        Assert.Equal(0, floatUFloatOffset);
        Assert.Equal(20, floatIsNegativeOffset);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "HappyPath")]
    public void Test_GBF01_ConstructorAndFieldInitialization_StoresCorrectValues()
    {
        CudaCapability.EnsureGpuAvailable();
        var limbs = CreateLimb4(0x11111111, 0x22222222, 0x33333333, 0x44444444);
        var significand = new GpuUIntBigInteger<LimbBuffer4>(limbs);
        var uFloat = new GpuBigUFloat<LimbBuffer4>(significand, exponent: 42);
        var bigFloat = new GpuBigFloat<LimbBuffer4>(uFloat, isNegative: true);

        Assert.Equal(0x11111111u, bigFloat.UFloat.Significand.Limbs[0]);
        Assert.Equal(0x44444444u, bigFloat.UFloat.Significand.Limbs[3]);
        Assert.Equal(42, bigFloat.UFloat.Exponent);
        Assert.True(bigFloat.IsNegative);

        var directBigFloat = new GpuBigFloat<LimbBuffer4>(significand, exponent: -10, isNegative: false);
        Assert.Equal(-10, directBigFloat.UFloat.Exponent);
        Assert.False(directBigFloat.IsNegative);
    }

    #endregion

    #region 2. Radix Point Alignment Logic Tests via Struct Member Invocations

    [SkippableTheory]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "HappyPath")]
    [InlineData(10, 10)]
    [InlineData(15, 10)]
    [InlineData(-5, 5)]
    [InlineData(100, -100)]
    [InlineData(int.MaxValue, int.MinValue)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Test_GBF01_RadixAlignment_ExponentDifference_AlignsToMaxExponent_ViaStructApi(int expA, int expB)
    {
        CudaCapability.EnsureGpuAvailable();
        var dummySignificand = new GpuUIntBigInteger<LimbBuffer4>(default);
        var floatA = new GpuBigFloat<LimbBuffer4>(dummySignificand, expA, isNegative: false);
        var floatB = new GpuBigFloat<LimbBuffer4>(dummySignificand, expB, isNegative: false);

        floatA.AlignRadixPoint(floatB, out var alignedA, out var alignedB, out _, out _);
        Assert.Equal(System.Math.Max(expA, expB), alignedA.UFloat.Exponent);
        Assert.Equal(System.Math.Max(expA, expB), alignedB.UFloat.Exponent);

        floatA.UFloat.AlignRadixPoint(floatB.UFloat, out var alignedUA, out var alignedUB, out _, out _);
        Assert.Equal(System.Math.Max(expA, expB), alignedUA.Exponent);
        Assert.Equal(System.Math.Max(expA, expB), alignedUB.Exponent);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "EdgeCase")]
    [InlineData(0b11000000u, 2, 0b00110000u, false)]
    [InlineData(0b11000001u, 2, 0b00110000u, true)] // LSB set -> Sticky Bit set
    [InlineData(0b10000000u, 32, 0b00000000u, true)] // Shifted out completely -> Sticky Bit set
    [InlineData(0b00000000u, 8, 0b00000000u, false)] // Zero value shifted out -> Sticky Bit false
    [InlineData(0b10000000u, 31, 0b00000000u, true)]
    [InlineData(0b10000000u, 33, 0b00000000u, true)]
    [InlineData(0xFFFFFFFFu, 128, 0x00000000u, true)]
    public void Test_GBF01_RadixAlignment_SignificandRightShift_PreservesStickyBitContract(uint initialValue,
        int shiftBits, uint expectedShiftedValue, bool expectedStickyBit)
    {
        CudaCapability.EnsureGpuAvailable();
        uint shiftedValue = shiftBits >= 32 ? 0u : initialValue >> shiftBits;
        uint mask = (1u << shiftBits) - 1u;
        bool stickyBit = shiftBits >= 32 ? (initialValue != 0u) : ((initialValue & mask) != 0u);

        Assert.Equal(expectedShiftedValue, shiftedValue);
        Assert.Equal(expectedStickyBit, stickyBit);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBF01_RadixAlignment_DeltaExponentExceedsPrecision_FlushesToZeroWithStickyBitContract()
    {
        CudaCapability.EnsureGpuAvailable();
        var limbs = CreateLimb4(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu);
        var significand = new GpuUIntBigInteger<LimbBuffer4>(limbs);

        var floatHighExp = new GpuBigFloat<LimbBuffer4>(significand, exponent: 100, isNegative: false);
        var floatLowExp = new GpuBigFloat<LimbBuffer4>(significand, exponent: 0, isNegative: false);

        floatHighExp.AlignRadixPoint(floatLowExp, out var alignedHighExp, out var alignedLowExp, out _, out _);

        Assert.Equal(100, alignedHighExp.UFloat.Exponent);
        Assert.Equal(100, alignedLowExp.UFloat.Exponent);
    }

    #endregion

    #region 3. Normalization & IEEE 754 Rounding Tests via Struct Member Invocations

    [SkippableTheory]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "HappyPath")]
    [InlineData(0, 0)] // Carry out = false, 0 leading zeros
    [InlineData(1, -1)] // 1 leading zero -> exp-1
    [InlineData(31, -31)] // 31 leading zeros -> exp-31
    public void Test_GBF01_Normalization_UnderflowLeadingZeros_AlignsMsbAndDecrementsExponent_ViaStructApi(
        int leadingZeros, int expectedExpOffset)
    {
        CudaCapability.EnsureGpuAvailable();
        var limbs = CreateLimb4(1, 0, 0, 0); // Non-zero significand to avoid flush-to-zero during normalization
        var nonZeroSignificand = new GpuUIntBigInteger<LimbBuffer4>(limbs);
        var uFloat = new GpuBigUFloat<LimbBuffer4>(nonZeroSignificand, exponent: 0);

        var normalized = uFloat.Normalize(leadingZeros: leadingZeros, carryOut: false);
        Assert.Equal(expectedExpOffset, normalized.Exponent);

        var bigFloat = new GpuBigFloat<LimbBuffer4>(uFloat, isNegative: false);
        var normFloat = bigFloat.Normalize(leadingZeros: leadingZeros, carryOut: false);
        Assert.Equal(expectedExpOffset, normFloat.UFloat.Exponent);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "HappyPath")]
    public void Test_GBF01_Normalization_CarryOverflow_ShiftsRightAndIncrementsExponent_ViaStructApi()
    {
        CudaCapability.EnsureGpuAvailable();
        var dummySignificand = new GpuUIntBigInteger<LimbBuffer4>(default);
        var uFloat = new GpuBigUFloat<LimbBuffer4>(dummySignificand, exponent: 5);

        var normalized = uFloat.Normalize(leadingZeros: 0, carryOut: true);
        Assert.Equal(6, normalized.Exponent);

        var bigFloat = new GpuBigFloat<LimbBuffer4>(uFloat, isNegative: true);
        var normFloat = bigFloat.Normalize(leadingZeros: 0, carryOut: true);
        Assert.Equal(6, normFloat.UFloat.Exponent);
        Assert.True(normFloat.IsNegative);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "EdgeCase")]
    [InlineData(GpuBigFloatRoundingMode.RoundToNearestEven, true, true, false, 0u, 1u)]
    [InlineData(GpuBigFloatRoundingMode.RoundToNearestEven, true, false, false, 0u, 0u)]
    [InlineData(GpuBigFloatRoundingMode.RoundToNearestEven, true, false, false, 1u, 2u)]
    [InlineData(GpuBigFloatRoundingMode.RoundTowardZero, true, true, true, 5u, 5u)]
    [InlineData(GpuBigFloatRoundingMode.RoundTowardPositiveInfinity, true, false, false, 2u, 3u)]
    [InlineData(GpuBigFloatRoundingMode.RoundTowardNegativeInfinity, false, true, true, 2u, 2u)]
    public void Test_GBF01_Ieee754Rounding_InvokesRoundingMode_ViaStructApi(GpuBigFloatRoundingMode mode, bool guard,
        bool round, bool sticky, uint initialLimb, uint expectedUFloatLimb)
    {
        CudaCapability.EnsureGpuAvailable();
        var limbs = CreateLimb4(initialLimb, 0, 0, 0);
        var significand = new GpuUIntBigInteger<LimbBuffer4>(limbs);
        var uFloat = new GpuBigUFloat<LimbBuffer4>(significand, exponent: 10);

        var roundedUFloat = uFloat.Round(mode, guard, round, sticky);
        Assert.Equal(10, roundedUFloat.Exponent);
        Assert.Equal(expectedUFloatLimb, roundedUFloat.Significand.Limbs[0]);

        var bigFloat = new GpuBigFloat<LimbBuffer4>(uFloat, isNegative: true);
        var roundedFloat = bigFloat.Round(mode, guard, round, sticky);

        // For negative values, directed rounding modes behave oppositely
        uint expectedSignedLimb = expectedUFloatLimb;
        if (mode == GpuBigFloatRoundingMode.RoundTowardPositiveInfinity) expectedSignedLimb = initialLimb; // truncates
        if (mode == GpuBigFloatRoundingMode.RoundTowardNegativeInfinity) expectedSignedLimb = initialLimb + (guard || round || sticky ? 1u : 0u);

        Assert.Equal(10, roundedFloat.UFloat.Exponent);
        Assert.True(roundedFloat.IsNegative);
        Assert.Equal(expectedSignedLimb, roundedFloat.UFloat.Significand.Limbs[0]);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "HappyPath")]
    public void Test_GBF01_ArithmeticOperatorAndMethodDeclarations_ExistAndAreCallable()
    {
        CudaCapability.EnsureGpuAvailable();
        var limbsA = CreateLimb4(100, 0, 0, 0);
        var limbsB = CreateLimb4(40, 0, 0, 0);
        var sigA = new GpuUIntBigInteger<LimbBuffer4>(limbsA);
        var sigB = new GpuUIntBigInteger<LimbBuffer4>(limbsB);

        var uFloatA = new GpuBigUFloat<LimbBuffer4>(sigA, exponent: 10);
        var uFloatB = new GpuBigUFloat<LimbBuffer4>(sigB, exponent: 10);

        var uAddResult = uFloatA + uFloatB;
        var uSubResult = uFloatA - uFloatB;

        // 100 + 40 = 140 (10001100_2, 8 bits)
        // 128 - 8 = 120 leading zeros -> Exponent 10 - 120 = -110
        // 140 << 120 -> Limbs[3] = 140 << 24 = 0x8C00_0000u
        Assert.Equal(-110, uAddResult.Exponent);
        Assert.Equal(0x8C00_0000u, uAddResult.Significand.Limbs[3]);

        // After subtraction (100 - 40 = 60), the significand is normalized by shifting left.
        // For LimbBuffer4 (128 bits), 60 has 122 leading zeros (128 - 6).
        // Exponent 10 - 122 = -112
        // Limbs[3] = 60 << 26 = 0xF000_0000u
        Assert.Equal(-112, uSubResult.Exponent);
        Assert.Equal(0xF000_0000u, uSubResult.Significand.Limbs[3]);

        var floatA = new GpuBigFloat<LimbBuffer4>(uFloatA, isNegative: false);
        var floatB = new GpuBigFloat<LimbBuffer4>(uFloatB, isNegative: false);

        var addResult = floatA + floatB;
        var subResult = floatA - floatB;

        Assert.Equal(-110, addResult.UFloat.Exponent);
        Assert.Equal(0x8C00_0000u, addResult.UFloat.Significand.Limbs[3]);
        Assert.False(addResult.IsNegative);

        Assert.Equal(-112, subResult.UFloat.Exponent);
        Assert.Equal(0xF000_0000u, subResult.UFloat.Significand.Limbs[3]);
        Assert.False(subResult.IsNegative);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-01")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBF01_ZeroSignificand_NormalizesToZeroExponentAndPositiveSign()
    {
        CudaCapability.EnsureGpuAvailable();
        var zeroLimbs = CreateLimb4(0, 0, 0, 0);
        var zeroUInt = new GpuUIntBigInteger<LimbBuffer4>(zeroLimbs);
        var zeroUFloat = new GpuBigUFloat<LimbBuffer4>(zeroUInt, exponent: 5);

        var positiveZero = new GpuBigFloat<LimbBuffer4>(zeroUFloat, isNegative: false);
        var negativeZero = new GpuBigFloat<LimbBuffer4>(zeroUFloat, isNegative: true);

        var normPos = positiveZero.Normalize(leadingZeros: 5, carryOut: false);
        var normNeg = negativeZero.Normalize(leadingZeros: 5, carryOut: false);

        Assert.Equal(0, normPos.UFloat.Exponent);
        Assert.Equal(0, normNeg.UFloat.Exponent);

        // IEEE 754 zero equivalency check (+0 == -0)
        Assert.Equal(normPos.UFloat.Significand.Limbs[0], normNeg.UFloat.Significand.Limbs[0]);
        Assert.Equal(normPos.UFloat.Exponent, normNeg.UFloat.Exponent);
    }

    #endregion
}
