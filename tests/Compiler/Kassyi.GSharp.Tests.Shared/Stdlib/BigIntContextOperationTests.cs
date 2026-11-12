#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// The context overloads of the shifts and bitwise operations of <c>GpuBigIntKernels</c> on the native target (CPU or
/// CUDA), against <see cref="BigInteger"/> on the same words; a null left operand throws
/// <see cref="NullReferenceException"/> at the <c>Gpu.NotNull</c> that reads it.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class BigIntContextOperationTests : TranspilerTestBase
{
    private const int Words = 4;
    private static readonly uint[] s_left = [0x80000001u, 0x00000003u, 0xF0F0F0F0u, 0x12345678u];
    private static readonly uint[] s_right = [0xFFFF0000u, 0x0F0F0F0Fu, 0x00000000u, 0xFFFFFFFFu];

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 0)]
    public void ShiftLeft_Words_MatchesBigIntegerShift(int wordShift, int bitShift)
    {
        var result = new uint[Words];

        BigIntContextOperationKernels.LaunchShiftLeftKernel(Context(s_left, null, result), wordShift, bitShift,
            blockDimX: 2);

        Assert.Equal(ToWords(ToBigInteger(s_left) << (wordShift * 32 + bitShift)), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 0)]
    public void ShiftRight_Words_MatchesBigIntegerShift(int wordShift, int bitShift)
    {
        var result = new uint[Words];

        BigIntContextOperationKernels.LaunchShiftRightKernel(Context(s_left, null, result), wordShift, bitShift,
            blockDimX: 2);

        Assert.Equal(ToWords(ToBigInteger(s_left) >> (wordShift * 32 + bitShift)), result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void BitwiseOperations_Words_MatchEachWord()
    {
        var and = new uint[Words];
        var or = new uint[Words];
        var xor = new uint[Words];
        var not = new uint[Words];

        BigIntContextOperationKernels.LaunchAndKernel(Context(s_left, s_right, and), blockDimX: 2);
        BigIntContextOperationKernels.LaunchOrKernel(Context(s_left, s_right, or), blockDimX: 2);
        BigIntContextOperationKernels.LaunchXorKernel(Context(s_left, s_right, xor), blockDimX: 2);
        BigIntContextOperationKernels.LaunchNotKernel(Context(s_left, null, not), blockDimX: 2);

        for (var i = 0; i < Words; i++)
        {
            Assert.Equal(s_left[i] & s_right[i], and[i]);
            Assert.Equal(s_left[i] | s_right[i], or[i]);
            Assert.Equal(s_left[i] ^ s_right[i], xor[i]);
            Assert.Equal(~s_left[i], not[i]);
        }
    }

    // The device function reads the operand through Gpu.NotNull: the native CPU build names the place in the message
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void ShiftLeft_NullLeftOperand_ThrowsNullReference()
    {
        Skip.If(IsCudaDialect, "The CUDA null cases run as differential cases (NullArrayAndFieldDifferentialTests).");
        var result = new uint[Words];

        var exception = Assert.Throws<NullReferenceException>(() =>
            BigIntContextOperationKernels.LaunchShiftLeftKernel(Context(null, null, result), 0, 1, blockDimX: 2));

        Assert.StartsWith("G#: Gpu.NotNull was given a null array at GpuBigIntKernels.cu.cs(", exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(new uint[Words], result);
    }

    private static GpuBigIntContext Context(uint[]? left, uint[]? right, uint[] result) => new()
    {
        Operands = new OperandContext
        {
            LeftOperand = left, RightOperand = right, TargetOperand = [], ResultBuffer = result
        },
        NumberOfWords = Words
    };

    private static BigInteger ToBigInteger(uint[] words)
    {
        var value = BigInteger.Zero;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | words[i];
        }

        return value;
    }

    // The low Words words of value
    private static uint[] ToWords(BigInteger value)
    {
        var words = new uint[Words];
        for (var i = 0; i < Words; i++)
        {
            words[i] = (uint)(value & uint.MaxValue);
            value >>= 32;
        }

        return words;
    }
}
