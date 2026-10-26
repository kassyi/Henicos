using GpuBigFloat = Kassyi.GSharp.Stdlib.GpuMath.BigNum.GpuBigFloat;
using GpuBigUInt = Kassyi.GSharp.Stdlib.GpuMath.BigNum.GpuBigUInt;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatObjTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BigFloat_Obj()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        
        using var sig = new GpuBigUInt(ctx, 4);
        using var a = new GpuBigFloat(sig, 4, false);
        
        Assert.Equal(4, a.UFloat.Significand.NumWords);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IGpuBigNumber_UnifiedComparison_ZeroAlloc_And_Boundary()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        // 1. +0 == -0
        using var zeroMag1 = new GpuBigUInt(ctx, new uint[] { 0, 0 });
        using var zeroMag2 = new GpuBigUInt(ctx, new uint[] { 0 });
        using var posZero = new GpuBigFloat(zeroMag1, 10, false);
        using var negZero = new GpuBigFloat(zeroMag2, -5, true);
        Assert.Equal(0, posZero.CompareTo(negZero));
        Assert.True(posZero == negZero);

        // 2. Cross-word virtual shift comparison:
        // valA = 0x80000001 * 2^1 = 0x1_00000002 * 2^0
        using var magA = new GpuBigUInt(ctx, new uint[] { 0x80000001u });
        using var magB = new GpuBigUInt(ctx, new uint[] { 2u, 1u });
        using var magC = new GpuBigUInt(ctx, new uint[] { 3u, 1u });
        using var floatA = new GpuBigFloat(magA, 1, false);
        using var floatB = new GpuBigFloat(magB, 0, false);
        using var floatC = new GpuBigFloat(magC, 0, false);

        Assert.Equal(0, floatA.CompareTo(floatB));
        Assert.True(floatA == floatB);
        Assert.True(floatA < floatC);
        Assert.True(floatC > floatA);

        // 3. Negative comparisons
        using var negA = new GpuBigFloat(magA, 1, true);
        using var negC = new GpuBigFloat(magC, 0, true);
        Assert.True(negC < negA);
        Assert.True(negA > negC);

        // 4. Generic IGpuBigNumber<T> contract check across all 4 arbitrary-precision types
        static int GenericCompare<T>(T x, T y) where T : struct, IGpuBigNumber<T>
            => GpuBigNumberAlgorithms.Compare(x, y);

        Assert.Equal(0, GenericCompare(floatA, floatB));
        Assert.True(GenericCompare(magA, magB) < 0);

        // 5. Fixed-width IGpuNumber<TSelf> contract check
        GpuUInt128 u128 = [0u, 0u, 1u, 0u];
        Assert.False(u128.IsZero());
        Assert.False(u128.IsNegative);
        Assert.False(u128.CanBeNegative);
        Assert.Equal(65, u128.GetBitLength());
    }

    private static void AssertExactValue(BigInteger expected, GpuBigFloat actual)
    {
        var sig = actual.UFloat.Significand.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(sig, actual.Exponent, actual.IsNegative, expected, 1),
            $"expected {expected}, actual {BigFloatReference.Describe(sig, actual.Exponent, actual.IsNegative)}");
    }

    // The named methods compute the same value as the operator they name; signs follow the usual rules.
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloat_NamedArithmetic_And_DivideByUInt32()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigFloat(new GpuBigUInt(ctx, [12u]), 0, false);
        using var b = new GpuBigFloat(new GpuBigUInt(ctx, [3u]), 0, true);

        // mutant: GpuBigFloat.cs:159 / 166 / 173 / 180 `this + other` -> `this - other` and the like
        using var sum = a.Add(b);
        using var difference = a.Subtract(b);
        using var product = a.Multiply(b);
        using var quotient = a.Divide(b);
        AssertExactValue(9, sum);
        AssertExactValue(15, difference);
        AssertExactValue(-36, product);
        AssertExactValue(-4, quotient);

        // -12 / 5 = -2 remainder 2 on the magnitude; the sign and the exponent are kept.
        // mutant: GpuBigFloat.cs:138 body of DivideByUInt32 removed (returns default)
        using var negTwelve = new GpuBigFloat(new GpuBigUInt(ctx, [12u]), 0, true);
        var (q, remainder) = negTwelve.DivideByUInt32(5);
        using (q)
        {
            Assert.Equal(new BigInteger(2), q.UFloat.Significand.ToBigInteger());
            Assert.Equal(0, q.Exponent);
            Assert.True(q.IsNegative);
            Assert.Equal(2u, remainder);
        }
    }

    public static TheoryData<uint[], int, bool, uint[], int, bool, int> CompareCases => new()
    {
        // Same scale, the bit lengths differ by 32: the right reader shifts by one whole word.
        // 2^40 vs 257 * 2^32 = 2^40 + 2^32. mutant: GpuBigNumberAlgorithms.cs:18 `- _wordShift` -> `+ _wordShift`
        { [0u, 256u], 0, false, [257u], 32, false, -1 },
        // Both negative, different scales: the larger magnitude is the smaller value.
        // mutant: GpuBigNumberAlgorithms.cs:89 `left.IsNegative ? -1 : 1` -> `1 : -1` / `true`
        { [1u], 10, true, [1u], 5, true, -1 },
        // +0 and -0 with the same exponent are equal (fast path).
        // mutant: GpuBigNumberAlgorithms.cs:56-58 `return 0;` removed
        { [0u], 3, false, [0u], 3, true, 0 },
        // Zero on the left with a different exponent: the sign of the right operand decides.
        // mutant: GpuBigNumberAlgorithms.cs:76 `right.IsNegative ? 1 : -1` mutated (`true` / `false` / `+1`)
        { [0u], 0, false, [1u], 5, true, 1 },
        { [0u], 0, false, [1u], 5, false, -1 },
        // Zero on the right: the sign of the left operand decides.
        // mutant: GpuBigNumberAlgorithms.cs:81 `left.IsNegative ? -1 : 1` mutated
        { [1u], 5, false, [0u], 0, false, 1 },
        { [1u], 5, true, [0u], 0, false, -1 },
        // Different signs, different exponents: the negative one is smaller, whatever the magnitudes.
        // mutant: GpuBigNumberAlgorithms.cs:86 `left.IsNegative ? -1 : 1` mutated
        { [1u], 0, false, [1u], 5, true, 1 },
        { [1u], 5, true, [1u], 0, false, -1 },
    };

    // Compare returns the sign of left - right, for every combination of zero, sign and scale.
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(CompareCases))]
    public void Compare_Table(
        uint[] leftWords, int leftExp, bool leftNeg, uint[] rightWords, int rightExp, bool rightNeg, int expected)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        // The constructor keeps the sign of a zero significand as given, so -0 is representable here.
        using var left = new GpuBigFloat(new GpuBigUInt(ctx, leftWords), leftExp, leftNeg);
        using var right = new GpuBigFloat(new GpuBigUInt(ctx, rightWords), rightExp, rightNeg);

        Assert.Equal(expected, Math.Sign(GpuBigNumberAlgorithms.Compare(left, right)));
        // Antisymmetry: swapping the operands flips the sign.
        Assert.Equal(-expected, Math.Sign(GpuBigNumberAlgorithms.Compare(right, left)));

        // The strict operators agree with Compare; equal values (+0 and -0) are neither less nor greater.
        // mutant: GpuBigFloat.cs:236 `< 0` -> `<= 0`, L244 `> 0` -> `>= 0`
        Assert.Equal(expected < 0, left < right);
        Assert.Equal(expected > 0, left > right);
    }
}
