using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The carry, funnel shift and bit reverse intrinsics compute the CUDA results on every target.</summary>
[Category(TestCategory.Syntax)]
public class CarryAndBitIntrinsicTests : TranspilerTestBase
{
    private const int Length = 64;

    private static readonly uint[] s_edgeValues = [0u, 1u, 0xFFFFFFFFu, 0x80000000u, 0x7FFFFFFFu, 0x12345678u];

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitIntrinsicsKernel_MatchesReference()
    {
        var input = CreateValues(seed: 0x9E3779B9u);
        var output = new uint[Length * 3];

        CarryAndBitIntrinsicKernels.LaunchBitIntrinsicsKernel(input, output, Length, blockDimX: Length);

        for (int i = 0; i < Length; i++)
        {
            uint value = input[i];
            uint next = input[(i + 1) % Length];
            int shift = i & 31;
            ulong pair = ((ulong)next << 32) | value;
            Assert.Equal(ReverseBits(value), output[i * 3]);
            Assert.Equal((uint)((pair << shift) >> 32), output[i * 3 + 1]);
            Assert.Equal((uint)(pair >> shift), output[i * 3 + 2]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CarryChainKernel_PropagatesCarryAndBorrow()
    {
        var a = CreateValues(seed: 0x85EBCA6Bu);
        var b = CreateValues(seed: 0xC2B2AE35u);
        var sum = new uint[Length];
        var carryOut = new ulong[Length];
        var difference = new uint[Length];
        var borrowOut = new long[Length];

        CarryAndBitIntrinsicKernels.LaunchCarryChainKernel(a, b, sum, carryOut, difference, borrowOut,
            blockDimX: Length);

        for (int i = 0; i < Length; i++)
        {
            uint carryIn = (uint)(i & 1);
            ulong wide = (ulong)a[i] + b[i] + carryIn;
            Assert.Equal((uint)wide, sum[i]);
            Assert.Equal(wide >> 32, carryOut[i]);

            Assert.Equal(a[i] - b[i] - carryIn, difference[i]);
            Assert.Equal((ulong)a[i] < (ulong)b[i] + carryIn ? 1L : 0L, borrowOut[i]);
        }
    }

    // Edge values first, then a deterministic xorshift sequence
    private static uint[] CreateValues(uint seed)
    {
        var values = new uint[Length];
        uint state = seed;
        for (int i = 0; i < Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            values[i] = i < s_edgeValues.Length ? s_edgeValues[(i + (int)(seed & 3u)) % s_edgeValues.Length] : state;
        }

        return values;
    }

    private static uint ReverseBits(uint value)
    {
        uint result = 0u;
        for (int bit = 0; bit < 32; bit++)
        {
            result = (result << 1) | ((value >> bit) & 1u);
        }

        return result;
    }
}
