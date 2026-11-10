#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Shared test helper methods and test data for GpuBigFloat tests.
/// </summary>
internal static class GpuBigFloatTestHelpers
{
    internal static readonly Dictionary<string, (uint[] Words, int Exponent, bool IsNegative)> s_setElementCases = new()
    {
        // 2^31 as a 6-word significand whose only set bit is its top bit (#629: the copy of the low 4 words stored 0).
        ["SixWordsTopBitOnly"] = ([0u, 0u, 0u, 0u, 0u, 0x8000_0000u], -160, false),
        // The top two words are zero: the value has 128 bits and must be stored exactly.
        ["SixWordsThatFit"] = ([0x1234_5678u, 0x9ABC_DEF0u, 0x0FED_CBA9u, 0x8765_4321u, 0u, 0u], 5, false),
        // Set bits below the top 128 bits are dropped toward zero; the sign is kept.
        ["SixWordsTruncated"] = ([0xDEAD_BEEFu, 0x1234_5678u, 1u, 2u, 3u, 0x4000_0000u], -7, true),
        // Zero in 6 words is stored as positive zero.
        ["SixWordsZero"] = ([0u, 0u, 0u, 0u, 0u, 0u], 9, true),
        // A value of 4 words or fewer is copied as it is.
        ["TwoWords"] = ([7u, 9u], -3, false),
        ["FourWords"] = ([1u, 2u, 3u, 4u], 11, true),
    };

    public static TheoryData<string> SetElementCases()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_setElementCases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    public static GpuBigFloat CreateFloat(GpuWorkspace context, long value, int exponent = 0, int numWords = 4)
    {
        var isNeg = value < 0;
        var mag = (ulong)(isNeg ? -value : value);
        var words = new uint[numWords];
        words[0] = (uint)(mag & 0xFFFFFFFFu);
        if (numWords > 1)
        {
            words[1] = (uint)(mag >> 32);
        }

        var sig = new GpuBigUInt(context, words);
        return new GpuBigFloat(sig, exponent, isNeg);
    }

    public static double ToDouble(GpuBigFloatView value)
    {
        var mag = (double)value.Significand.ToBigInteger() * Math.Pow(2.0, value.Exponent);
        return value.IsNegative ? -mag : mag;
    }

    // The element equals num / den exactly, and zero is stored as positive zero.
    public static void AssertElement(scoped GpuBigFloatView element, BigInteger num, BigInteger den, string label)
    {
        var sig = element.Significand.ToBigInteger();
        var actualDesc = BigFloatReference.Describe(sig, element.Exponent, element.IsNegative);
        Assert.True(
            BigFloatReference.EqualsRational(sig, element.Exponent, element.IsNegative, num, den)
            && (!element.IsNegative || !sig.IsZero),
            $"{label}: expected {num}/{den}, actual {actualDesc}");
    }

    public static BigInteger FromWords(uint[] words)
    {
        var value = BigInteger.Zero;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | words[i];
        }

        return value;
    }

    public static void AssertExactSum<T>(string operation, BigInteger num, BigInteger den, T actual)
        where T : struct, IGpuBigNumber<T>
    {
        var sig = actual.Magnitude.ToBigInteger();
        var actualDesc = BigFloatReference.Describe(sig, actual.Exponent, actual.IsNegative);
        Assert.True(
            BigFloatReference.EqualsRational(sig, actual.Exponent, actual.IsNegative, num, den),
            $"{typeof(T).Name} {operation}: expected {num}/{den}, actual {actualDesc}");
    }

    public static T CreateNumber<T>(GpuWorkspace context, uint value)
        where T : struct, IGpuBigNumber<T> =>
        T.Create(new GpuBigUInt(context, [value, 0u, 0u, 0u]), 0, false);
}
