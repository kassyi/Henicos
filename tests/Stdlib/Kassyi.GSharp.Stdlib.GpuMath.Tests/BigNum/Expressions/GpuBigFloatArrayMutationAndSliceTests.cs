#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions.GpuBigFloatTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatArrayMutationAndSliceTests : GpuBigFloatWorkspaceTestBase
{
    // A slice must lie inside the array; the outer check reports the offending range as `elementOffset`.
    // The inner GpuBigUIntArray.Slice throws the same exception type without a ParamName, so ParamName tells the
    // outer check apart from the inner one.
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    // mutant: GpuBigFloatArray.cs:147 `elementOffset < 0` -> `<= 0`, `> Length` -> `>= Length`
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    // mutant: GpuBigFloatArray.cs:147 `elementCount < 0` -> `<= 0`
    [InlineData(1, 0)]
    public void GpuBigFloatArray_Slice_InsideBounds_Succeeds(int offset, int count)
    {
        using var array = new GpuBigFloatArray(Context, 4, 2);

        // The slice is a view of the array's memory; it is not disposed separately.
        var slice = array.Slice(offset, count);

        Assert.Equal(count, slice.Length);
        Assert.Equal(count, slice.Exponents.Length);
        Assert.Equal(count, slice.Signs.Length);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    // mutant: GpuBigFloatArray.cs:147 `||` -> `&&` (either), L149 `throw` removed
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    // mutant: GpuBigFloatArray.cs:147 `elementOffset + elementCount` -> `elementOffset - elementCount`
    [InlineData(1, 4)]
    public void GpuBigFloatArray_Slice_OutsideBounds_Throws(int offset, int count)
    {
        using var array = new GpuBigFloatArray(Context, 4, 2);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => array.Slice(offset, count));
        Assert.Equal("elementOffset", error.ParamName);
    }

    // The three component arrays describe the same elements, so their lengths must agree.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_InternalCtor_MismatchedLengths_Throws()
    {
        // The constructor throws before it takes ownership, so the test disposes the arrays.
        using var significands = new GpuBigUIntArray(Context, 2, 4);
        using var exponents = new GpuBigUIntArray(Context, 2, 1);
        using var signs = new GpuBigUIntArray(Context, 3, 1);

        // mutant: GpuBigFloatArray.cs:72 `||` -> `&&`, L74 `throw` removed
        Assert.Throws<ArgumentException>(() => new GpuBigFloatArray(significands, exponents, signs));
    }

    // One-word significands: 0xFFFFFFFF * 2^32 - 1 aligns the left operand 32 bits up into the carry word, so the
    // difference fills the top bit of the result. Its sign comes from the borrow of the magnitude subtraction.
    // mutant: the sign read from the top bit of the result instead of the borrow (-4294967297)
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_Subtract_DifferenceReachesTheTopBit_TakesTheSignFromTheBorrow()
    {
        using var left = new GpuBigFloatArray(Context, 2, 1);
        using var right = new GpuBigFloatArray(Context, 2, 1);
        using var big = CreateFloat(0xFFFFFFFFL, exponent: 32, numWords: 1);
        using var one = CreateFloat(1, numWords: 1);
        left.SetElement(0, big);
        left.SetElement(1, one);
        right.SetElement(0, one);
        right.SetElement(1, big);

        using var zip = left.AsVectorizedEnumerable().ZipSubtract(right.AsVectorizedEnumerable());
        var zipResult = zip.Evaluate(Context);
        using var select = left.AsVectorizedEnumerable().SelectSubtract(one);
        var selectResult = select.Evaluate(Context);

        var difference = (BigInteger.One << 64) - (BigInteger.One << 32) - 1;
        AssertElement(zipResult.GetElement(0), difference, 1, "ZipSubtract element 0");
        AssertElement(zipResult.GetElement(1), -difference, 1, "ZipSubtract element 1");
        AssertElement(selectResult.GetElement(0), difference, 1, "SelectSubtract element 0");
        AssertElement(selectResult.GetElement(1), 0, 1, "SelectSubtract element 1");
    }

    // A zero divisor is an error, as the documentation of SelectDivide and ZipDivide states, never a quotient of 0.
    // mutant: the host check of the scalar removed ([0, 0]); the kernel no longer records a zero element ([3, 0])
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_Divide_ZeroDivisor_ThrowsDivideByZeroException()
    {
        using var dividends = new GpuBigFloatArray(Context, 2, 4);
        using var divisors = new GpuBigFloatArray(Context, 2, 4);
        using var three = CreateFloat(3);
        using var five = CreateFloat(5);
        using var one = CreateFloat(1);
        using var zero = CreateFloat(0);
        dividends.SetElement(0, three);
        dividends.SetElement(1, five);
        divisors.SetElement(0, one);
        divisors.SetElement(1, zero);

        using var byScalar = dividends.AsVectorizedEnumerable().SelectDivide(zero);
        using var byVector = dividends.AsVectorizedEnumerable().ZipDivide(divisors.AsVectorizedEnumerable());

        Assert.Throws<DivideByZeroException>(() => byScalar.Evaluate(Context));
        Assert.Throws<DivideByZeroException>(() => byVector.Evaluate(Context));
    }

    // SetElement replaces the whole element: a shorter value leaves no stale upper words, and it never writes
    // past the element into its neighbour.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_SetElement_ShorterAndLongerValues()
    {
        using var array = new GpuBigFloatArray(Context, 2, 4);

        // 1. A 4-word all-ones value, then a 1-word 5: the element reads back as exactly 5.
        // mutant: GpuBigFloatArray.cs:122 `valueWords < WordsPerElement` -> `>` / negated,
        // L124 ClearMemoryAsync removed
        using var allOnes = new GpuBigFloat(
            new GpuBigUInt(Context, [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu]), 0, false);
        using var five = new GpuBigFloat(new GpuBigUInt(Context, [5u]), 3, true);
        array.SetElement(0, allOnes);
        array.SetElement(0, five);

        // GetElement returns views of the array's memory; they are not disposed.
        var element0 = array.GetElement(0);
        Assert.Equal(new BigInteger(5), element0.Significand.ToBigInteger());
        Assert.Equal(3, element0.Exponent);
        Assert.True(element0.IsNegative);

        // 2. A 6-word value into element 0 must not touch element 1 (what element 0 holds is checked by
        // GpuBigFloatArray_SetElement_RoundsWideValuesTowardZero).
        // mutant: GpuBigFloatArray.cs:120 `valueWords <= WordsPerElement` ->
        // `valueWords <= WordsPerElement + 2` (copies 6 words over element 1)
        using var seven = new GpuBigFloat(new GpuBigUInt(Context, [7u, 0u, 0u, 0u]), -2, false);
        array.SetElement(1, seven);
        using var wide = new GpuBigFloat(new GpuBigUInt(Context, [1u, 2u, 3u, 4u, 5u, 6u]), 0, false);
        array.SetElement(0, wide);

        var element1 = array.GetElement(1);
        Assert.Equal(new BigInteger(7), element1.Significand.ToBigInteger());
        Assert.Equal(-2, element1.Exponent);
        Assert.False(element1.IsNegative);
    }

    public static TheoryData<string> SetElementCases() => GpuBigFloatTestHelpers.SetElementCases();

    // The element holds RZ_128(v): v itself when its significand has at most 128 bits, otherwise v rounded toward zero
    // to 128 bits (#629). The neighbouring elements do not change.
    // mutant: GpuBigFloatArray.cs:120 `valueWords <= WordsPerElement` -> `true` (SixWordsTopBitOnly,
    // SixWordsTruncated, SixWordsThatFit, SixWordsZero: the wide value is copied as it is, over its neighbour)
    // mutant: GpuBigFloatArray.cs:138 `exponent = rounded.Exponent` -> `exponent = value.Exponent`
    // (SixWordsTopBitOnly, SixWordsTruncated)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SetElementCases))]
    public void GpuBigFloatArray_SetElement_RoundsWideValuesTowardZero(string caseName)
    {
        var (words, exponent, isNegative) = GpuBigFloatTestHelpers.s_setElementCases[caseName];
        using var array = new GpuBigFloatArray(Context, 3, 4);
        using var before = new GpuBigFloat(new GpuBigUInt(Context, [11u, 0u, 0u, 0u]), -1, false);
        using var after = new GpuBigFloat(new GpuBigUInt(Context, [13u, 0u, 0u, 0u]), 2, true);
        array.SetElement(0, before);
        array.SetElement(2, after);

        using var value = new GpuBigFloat(new GpuBigUInt(Context, words), exponent, isNegative);
        array.SetElement(1, value);

        // The expected value comes from the input words: RZ_128(|v|) (|v| itself when it fits) with the sign of v.
        var (rzSig, rzExp) = BigFloatReference.RoundTowardZero(
            FromWords(words) << Math.Max(exponent, 0), BigInteger.One << Math.Max(-exponent, 0), 128);
        var expectedNum = rzExp >= 0 ? rzSig << (int)rzExp : rzSig;
        var expectedDen = rzExp >= 0 ? BigInteger.One : BigInteger.One << (int)-rzExp;

        // GetElement returns views of the array's memory; they are not disposed.
        var actualNum = isNegative ? -expectedNum : expectedNum;
        AssertElement(array.GetElement(1), actualNum, expectedDen, $"{caseName} element 1");
        AssertElement(array.GetElement(0), 11, 2, $"{caseName} element 0");
        AssertElement(array.GetElement(2), -52, 1, $"{caseName} element 2");
    }

    // Rounding a wide value moves its top bit to the top of the element and raises the exponent by the same shift; an
    // exponent above int.MaxValue throws instead of wrapping around (#661 §4.10).
    // mutant: GSharpKernelFloatProvider.cs:70 `checked((int)(exponent - shift))` -> `unchecked(...)`
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_SetElement_ExponentOutOfRange_Throws()
    {
        using var array = new GpuBigFloatArray(Context, 1, 4);

        // 6 words with the top bit set: the shift is 128 - 192 = -64, so the exponent would be int.MaxValue - 10 + 64.
        using var wide = new GpuBigFloat(
            new GpuBigUInt(Context, [1u, 0u, 0u, 0u, 0u, 0x8000_0000u]), int.MaxValue - 10, false);
        Assert.Throws<OverflowException>(() => array.SetElement(0, wide));

        // The largest exponent that fits is stored: int.MaxValue - 64 + 64.
        using var fits = new GpuBigFloat(
            new GpuBigUInt(Context, [1u, 0u, 0u, 0u, 0u, 0x8000_0000u]), int.MaxValue - 64, false);
        array.SetElement(0, fits);
        Assert.Equal(int.MaxValue, array.GetElement(0).Exponent);
    }
}
