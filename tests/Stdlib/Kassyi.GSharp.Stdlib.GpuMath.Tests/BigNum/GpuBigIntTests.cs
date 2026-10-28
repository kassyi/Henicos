
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntegerTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntegerTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Construct_WorksCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        using var gpuBigInt = new GpuBigUInt(_context, 4);
        var signed = new GpuBigInt(gpuBigInt, isNegative: true);

        Assert.True(signed.IsNegative);
        Assert.Equal(gpuBigInt, signed.Value);
    }

    // ---- Signed comparison, Create and the generic operators (#598 stage 2b) ----

    // -7, -3, +0, -0 (zero magnitude with the sign set), +3, +7. Index 2 and 3 are the two zeros.
    private static readonly (BigInteger Value, bool NegativeZero)[] s_signedValues =
    [
        (-7, false), (-3, false), (0, false), (0, true), (3, false), (7, false)
    ];

    public static TheoryData<int, int> SignedPairs()
    {
        var data = new TheoryData<int, int>();
        for (var left = 0; left < s_signedValues.Length; left++)
        {
            for (var right = 0; right < s_signedValues.Length; right++)
            {
                data.Add(left, right);
            }
        }

        return data;
    }

    private GpuBigInt CreateSigned(BigInteger value, bool negativeZero = false, int words = 2) =>
        new(GpuBigUInt.FromBigInteger(_context, BigInteger.Abs(value), words), value.Sign < 0 || negativeZero);

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SignedPairs))]
    public void ComparisonOperators_MatchBigIntegerOrder(int leftIndex, int rightIndex)
    {
        var (leftValue, leftNegativeZero) = s_signedValues[leftIndex];
        var (rightValue, rightNegativeZero) = s_signedValues[rightIndex];
        using var left = CreateSigned(leftValue, leftNegativeZero);
        using var right = CreateSigned(rightValue, rightNegativeZero);

        // Every operator agrees with the BigInteger order; +0 and -0 are equal. The pairs include equal values, where
        // < and > must be false and <= and >= true. Equals is the identity of the buffers (#638), and the two operands
        // are always separate buffers.
        // mutant: GpuBigInt operator == `== 0` -> `!= 0`; operator != `!= 0` -> `== 0`
        // mutant: GpuBigInt operator < / > / <= / >= `< 0` -> `<= 0`, `> 0` -> `>= 0`, `<= 0` -> `< 0`, `>= 0` -> `> 0`
        var order = leftValue.CompareTo(rightValue);
        Assert.Equal(
            (order == 0, order != 0, order < 0, order > 0, order <= 0, order >= 0, false, false),
            (left == right, left != right, (left < right), (left > right), left <= right, left >= right, left.Equals(right), left.Equals((object)right))
        );
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Create_RejectsNonZeroExponentAndNormalizesNegativeZero()
    {
        // An integer has exponent 0.
        // mutant: GpuBigInt.cs:52 `exponent != 0` -> `exponent == 0`; :54 throw removed
        using var seven = GpuBigUInt.FromBigInteger(_context, 7, 2);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigInt.Create(seven, 1, false));
        Assert.Equal("exponent", error.ParamName);

        // The sign is kept for a non-zero magnitude and dropped for zero (no negative zero).
        // mutant: GpuBigInt.cs:57 `isNegative && !magnitude.IsZero()` -> `||` / `magnitude.IsZero()`
        using var zero = GpuBigUInt.FromBigInteger(_context, 0, 2);
        Assert.False(GpuBigInt.Create(zero, 0, true).IsNegative);
        Assert.True(GpuBigInt.Create(seven, 0, true).IsNegative);
        Assert.False(GpuBigInt.Create(seven, 0, false).IsNegative);
        Assert.False(GpuBigInt.Create(zero, 0, false).IsNegative);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Equals_IsIdentity_EqualityOperatorComparesValues()
    {
        // Equals and GetHashCode agree on the identity of the magnitude buffer and the sign (#638); == compares the
        // values, so +0 and -0 in two separate buffers are == but not Equals.
        // mutant: GpuBigInt.Equals compares the values again (positiveZero.Equals(negativeZero) becomes true)
        // mutant: GpuBigInt.Equals ignores IsNegative (sameBuffer.Equals(negated) becomes true)
        // mutant: GpuBigInt operator == calls Equals (positiveZero == negativeZero becomes false)
        using var positiveZero = CreateSigned(0);
        using var negativeZero = CreateSigned(0, negativeZero: true);
        var sameBuffer = new GpuBigInt(positiveZero.Value, positiveZero.IsNegative);
        var negated = new GpuBigInt(positiveZero.Value, !positiveZero.IsNegative);

        Assert.Equal(
            (false, true, true, true, false, 2),
            (positiveZero.Equals(negativeZero), positiveZero == negativeZero, positiveZero.Equals(sameBuffer),
                positiveZero.GetHashCode() == sameBuffer.GetHashCode(), sameBuffer.Equals(negated),
                new HashSet<GpuBigInt> { positiveZero, negativeZero, sameBuffer }.Count)
        );
    }

    public static TheoryData<string, string> GenericOperatorOperands() => new()
    {
        { "-123456789012345678901", "98765432109876543" },
        { "98765432109876543", "-12345" },
        { "-7", "-3" },
        { "7", "3" },
    };

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(GenericOperatorOperands))]
    public void GenericOperators_MatchBigInteger(string leftText, string rightText)
    {
        var leftValue = BigInteger.Parse(leftText);
        var rightValue = BigInteger.Parse(rightText);
        using var left = CreateSigned(leftValue, words: 4);
        using var right = CreateSigned(rightValue, words: 4);

        // The explicitly implemented IGpuBigNumber<GpuBigInt> operators evaluate to the BigInteger results
        // (division truncates toward zero).
        // mutant: GpuBigInt.cs:192 / :196 / :200 / :204 operator body replaced (Stryker: statement / arithmetic)
        using var sum = Add(left, right);
        using var difference = Subtract(left, right);
        using var product = Multiply(left, right);
        using var quotient = Divide(left, right);
        Assert.Equal(
            (leftValue + rightValue, leftValue - rightValue, leftValue * rightValue, BigInteger.Divide(leftValue, rightValue)),
            (ToBigInteger(sum), ToBigInteger(difference), ToBigInteger(product), ToBigInteger(quotient))
        );
    }

    private static T Add<T>(T left, T right) where T : struct, IGpuBigNumber<T> => left + right;

    private static T Subtract<T>(T left, T right) where T : struct, IGpuBigNumber<T> => left - right;

    private static T Multiply<T>(T left, T right) where T : struct, IGpuBigNumber<T> => left * right;

    private static T Divide<T>(T left, T right) where T : struct, IGpuBigNumber<T> => left / right;

    private static BigInteger ToBigInteger(GpuBigInt value) => value.Value.ToBigInteger(value.IsNegative);
}
