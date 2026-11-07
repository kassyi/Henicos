#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The float vector <c>Sum</c> takes the carry and the borrow of each accumulation from the carry out of the top word
/// of the element: a same-sign sum that carries out raises the exponent, and an opposite-sign sum that borrows takes
/// the sign of the larger magnitude.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigFloatVectorSumCarryTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatVectorSumCarryTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    // One-word significands with equal exponents: the sum of two full words carries out of the element width and is
    // kept by raising the exponent, and a sum of opposite signs takes its sign from the borrow, not from the top bit.
    // mutant: the carry out dropped (4294967294); the sign read from the top bit of the difference (+2)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(new long[] { 0xFFFFFFFFL, 0xFFFFFFFFL }, 8589934590L)]
    [InlineData(new long[] { 1, -0xFFFFFFFFL }, -4294967294L)]
    [InlineData(new long[] { -0xFFFFFFFFL, 1 }, -4294967294L)]
    [InlineData(new long[] { 0x80000000L, 0x80000000L, 0x80000000L, 0x80000000L }, 8589934592L)]
    [InlineData(new long[] { -0x80000000L, -0x80000000L, 0x7FFFFFFEL }, -2147483650L)]
    public void GpuBigFloatArray_Sum_OneWordElements_KeepsTheCarryAndTheSign(long[] values, long expected)
    {
        var elements = new BigInteger[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            elements[i] = values[i];
        }

        AssertSum("one-word sum", numWords: 1, elements, expected);
    }

    // Multi-word significands with equal exponents (no alignment shift): only the carry out of the top word decides
    // the carry and the borrow. 33 words pass one warp, so the carry crosses a warp boundary on its way up.
    public static TheoryData<string, int, BigInteger[], BigInteger> MultiWordCarryRows => new()
    {
        { "low word carries, top word does not", 2, [0xFFFFFFFFUL, 1], 0x1_0000_0000UL },
        { "carry runs through the top word", 2, [ulong.MaxValue, 1], BigInteger.One << 64 },
        { "difference without a borrow", 2, [0x1_0000_0000UL, -1], 0xFFFFFFFFUL },
        { "low words carry, 33 words", 33, [ulong.MaxValue, 1], BigInteger.One << 64 },
        {
            "carry through every word, 33 words",
            33,
            [(BigInteger.One << (33 * 32)) - 1, 1],
            BigInteger.One << (33 * 32)
        }
    };

    // mutant: the top carry taken from word 0 instead of word numWords - 1 (rows 1, 3, 4); the carry into the top
    // word left out of it, `topCarry[0] = g` (rows 2, 5)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(MultiWordCarryRows))]
    public void GpuBigFloatArray_Sum_MultiWordElements_TakesTheCarryOfTheTopWord(
        string label,
        int numWords,
        BigInteger[] values,
        BigInteger expected) =>
        AssertSum(label, numWords, values, expected);

    private void AssertSum(string label, int numWords, BigInteger[] values, BigInteger expected)
    {
        using var array = new GpuBigFloatArray(_workspace, values.Length, numWords);
        for (var i = 0; i < values.Length; i++)
        {
            var words = new uint[numWords];
            var magnitude = BigInteger.Abs(values[i]);
            for (var w = 0; w < numWords; w++)
            {
                words[w] = (uint)(magnitude & uint.MaxValue);
                magnitude >>= 32;
            }

            using var element = new GpuBigFloat(new GpuBigUInt(_workspace, words), 0, values[i].Sign < 0);
            array.SetElement(i, element);
        }

        using var sum = array.AsVectorizedEnumerable().Sum(_workspace);

        var significand = sum.Magnitude.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(significand, sum.Exponent, sum.IsNegative, expected, 1),
            $"{label}: expected {expected}, actual "
            + BigFloatReference.Describe(significand, sum.Exponent, sum.IsNegative));
    }
}
