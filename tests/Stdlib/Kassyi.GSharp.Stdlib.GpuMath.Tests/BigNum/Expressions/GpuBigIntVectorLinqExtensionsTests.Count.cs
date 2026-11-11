namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    // The elements of every case; a divisor vector with a zero in the middle, and one without
    private static readonly BigInteger[] s_countValues = [6, 9, 12];
    private static readonly BigInteger[] s_zeroInTheMiddle = [1, 0, 3];
    private static readonly BigInteger[] s_noZero = [1, 2, 3];

    public static TheoryData<string> CountChains =>
    [
        "SelectDivide(0)",
        "SelectModulo(0)",
        "SelectDivideByUInt32(0)",
        "SelectDivide(0).Take(2)",
        "SelectDivide(0).Skip(3)",
        "SelectDivide(0).Take(0)",
        "SelectDivide(0).SelectAdd(3)",
        "ZipAdd(SelectDivide(0))",
        "ZipDivide(zero in the middle)",
        "ZipModulo(zero in the middle)",
        "SelectMultiply(-1).SelectSqrt()",
        "SelectSqrt()",
        "SelectDivide(3)",
        "SelectDivideByUInt32(2)",
        "ZipDivide(no zero)",
        "SelectAdd(3).SelectMultiply(3)",
    ];

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(CountChains))]
    public void Count_Chain_AgreesWithSystemLinqAndEvaluate(string chain)
    {
        using var values = CreateArray(s_countValues);
        using var zeroInTheMiddle = CreateArray(s_zeroInTheMiddle);
        using var noZero = CreateArray(s_noZero);
        using var zero = CreateScalar(0);
        using var three = CreateScalar(3);
        using var minusOne = CreateScalar(-1);

        IGpuBigIntVectorExpression Build()
        {
            var v = values.AsVectorizedEnumerable();
            return chain switch
            {
                "SelectDivide(0)" => v.SelectDivide(zero),
                "SelectModulo(0)" => v.SelectModulo(zero),
                "SelectDivideByUInt32(0)" => v.SelectDivideByUInt32(0),
                "SelectDivide(0).Take(2)" => v.SelectDivide(zero).Take(2),
                "SelectDivide(0).Skip(3)" => v.SelectDivide(zero).Skip(3),
                "SelectDivide(0).Take(0)" => v.SelectDivide(zero).Take(0),
                "SelectDivide(0).SelectAdd(3)" => v.SelectDivide(zero).SelectAdd(three),
                "ZipAdd(SelectDivide(0))" => v.ZipAdd(values.AsVectorizedEnumerable().SelectDivide(zero)),
                "ZipDivide(zero in the middle)" => v.ZipDivide(zeroInTheMiddle.AsVectorizedEnumerable()),
                "ZipModulo(zero in the middle)" => v.ZipModulo(zeroInTheMiddle.AsVectorizedEnumerable()),
                "SelectMultiply(-1).SelectSqrt()" => v.SelectMultiply(minusOne).SelectSqrt(),
                "SelectSqrt()" => v.SelectSqrt(),
                "SelectDivide(3)" => v.SelectDivide(three),
                "SelectDivideByUInt32(2)" => v.SelectDivideByUInt32(2),
                "ZipDivide(no zero)" => v.ZipDivide(noZero.AsVectorizedEnumerable()),
                "SelectAdd(3).SelectMultiply(3)" => v.SelectAdd(three).SelectMultiply(three),
                _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, null)
            };
        }

        // System.Linq over the same elements: Count runs the projection of every element it counts. BigInteger has
        // no square root; the contract of SelectSqrt (InvalidOperationException for a negative element) stands in.
        int Reference()
        {
            var v = s_countValues;
            return chain switch
            {
                "SelectDivide(0)" => v.Select(static x => x / 0).Count(),
                "SelectModulo(0)" => v.Select(static x => x % 0).Count(),
                "SelectDivideByUInt32(0)" => v.Select(static x => x / 0u).Count(),
                "SelectDivide(0).Take(2)" => v.Select(static x => x / 0).Take(2).Count(),
                "SelectDivide(0).Skip(3)" => v.Select(static x => x / 0).Skip(3).Count(),
                "SelectDivide(0).Take(0)" => v.Select(static x => x / 0).Take(0).Count(),
                "SelectDivide(0).SelectAdd(3)" => v.Select(static x => x / 0).Select(static x => x + 3).Count(),
                "ZipAdd(SelectDivide(0))" => v.Zip(v.Select(static x => x / 0), static (a, b) => a + b).Count(),
                "ZipDivide(zero in the middle)" => v.Zip(s_zeroInTheMiddle, static (a, b) => a / b).Count(),
                "ZipModulo(zero in the middle)" => v.Zip(s_zeroInTheMiddle, static (a, b) => a % b).Count(),
                "SelectMultiply(-1).SelectSqrt()" => v.Select(static x => -x).Select(SqrtContract).Count(),
                "SelectSqrt()" => v.Select(SqrtContract).Count(),
                "SelectDivide(3)" => v.Select(static x => x / 3).Count(),
                "SelectDivideByUInt32(2)" => v.Select(static x => x / 2u).Count(),
                "ZipDivide(no zero)" => v.Zip(s_noZero, static (a, b) => a / b).Count(),
                "SelectAdd(3).SelectMultiply(3)" => v.Select(static x => (x + 3) * 3).Count(),
                _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, null)
            };
        }

        var expectedCount = 0;
        var expectedError = Record.Exception(() => expectedCount = Reference());

        var count = -1;
        var countError = Record.Exception(() =>
        {
            using var countExpr = Build().Count();
            using var result = countExpr.Evaluate(_context);
            count = (int)ToBigInt(result);
        });

        var length = -1;
        var evaluateError = Record.Exception(() =>
        {
            using var expr = Build();
            length = expr.Evaluate(_context).Length;
        });

        // Count throws what System.Linq throws, and what Evaluate of the same chain throws: the result of a chain does
        // not depend on the operator that ends it.
        // mutant: VectorCountExpression.Evaluate without the evaluation of a MayThrow vector (every throwing row)
        // mutant: VectorDivScalarExpression without `Divides => true` (SelectDivide(0) returns 3)
        // mutant: VectorSqrtExpression.ElementMayThrow => false (SelectMultiply(-1).SelectSqrt() returns 3)
        // mutant: VectorDivByUInt32Expression.ElementMayThrow => false (SelectDivideByUInt32(0) returns 3)
        // mutant: the Zip MayThrow without ChecksZeroDivisor (ZipDivide(zero in the middle) returns 3)
        // mutant: VectorTakeExpression.MayThrow = false (SelectDivide(0).Take(2) returns 2)
        // mutant: VectorBinaryScalarExpressionBase without the vector in _inputsMayThrow (SelectDivide(0).SelectAdd(3))
        // mutant: VectorBinaryVectorExpressionBase without the right input in _inputsMayThrow (ZipAdd(SelectDivide(0)))
        Assert.Equal(expectedError?.GetType(), countError?.GetType());
        Assert.Equal(expectedError?.GetType(), evaluateError?.GetType());
        if (expectedError is null)
        {
            // Without an error, the count is the number of elements System.Linq counts, and the evaluated length
            Assert.Equal(expectedCount, count);
            Assert.Equal(expectedCount, length);
        }
    }

    public static TheoryData<string, bool> MayThrowChains => new()
    {
        { "leaf", false },
        { "SelectAdd(3).SelectSubtract(3).SelectMultiply(3)", false },
        { "Take(2).Skip(1)", false },
        { "ZipAdd(same length).ZipMultiply(same length)", false },
        { "SelectDivideByUInt32(2)", false },
        { "SelectDivide(3)", true },
        { "SelectModulo(3)", true },
        { "SelectSqrt()", true },
        { "SelectDivideByUInt32(0)", true },
        { "ZipDivide(same length)", true },
        { "ZipAdd(other length)", true },
        { "SelectDivide(3).Take(1)", true },
        { "SelectDivide(3).SelectAdd(3)", true },
        { "ZipAdd(SelectDivide(3))", true },
    };

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(MayThrowChains))]
    public void MayThrow_Chain_IsDecidedPerNode(string chain, bool expected)
    {
        using var values = CreateArray(s_countValues);
        using var other = CreateArray(s_noZero);
        using var shorter = CreateArray([1, 2]);
        using var three = CreateScalar(3);
        var v = values.AsVectorizedEnumerable();
        using var expr = chain switch
        {
            "leaf" => v,
            "SelectAdd(3).SelectSubtract(3).SelectMultiply(3)" => v.SelectAdd(three).SelectSubtract(three)
                .SelectMultiply(three),
            "Take(2).Skip(1)" => v.Take(2).Skip(1),
            "ZipAdd(same length).ZipMultiply(same length)" => v.ZipAdd(other.AsVectorizedEnumerable())
                .ZipMultiply(other.AsVectorizedEnumerable()),
            "SelectDivideByUInt32(2)" => v.SelectDivideByUInt32(2),
            "SelectDivide(3)" => v.SelectDivide(three),
            "SelectModulo(3)" => v.SelectModulo(three),
            "SelectSqrt()" => v.SelectSqrt(),
            "SelectDivideByUInt32(0)" => v.SelectDivideByUInt32(0),
            "ZipDivide(same length)" => v.ZipDivide(other.AsVectorizedEnumerable()),
            "ZipAdd(other length)" => v.ZipAdd(shorter.AsVectorizedEnumerable()),
            "SelectDivide(3).Take(1)" => v.SelectDivide(three).Take(1),
            "SelectDivide(3).SelectAdd(3)" => v.SelectDivide(three).SelectAdd(three),
            "ZipAdd(SelectDivide(3))" => v.ZipAdd(other.AsVectorizedEnumerable().SelectDivide(three)),
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, null)
        };

        // Count answers from the length only for a chain of plain operations (no evaluation, no synchronization); a
        // chain with an operation that can throw is evaluated, whatever its values. The decision reads no value.
        // mutant: VectorEffects.MayThrow returning true for an array (leaf and every plain chain)
        // mutant: VectorDivByUInt32Expression.ElementMayThrow => true (SelectDivideByUInt32(2))
        // mutant: the Zip MayThrow without `Capacity != _rightCapacity` (ZipAdd(other length))
        // mutant: VectorModScalarExpression without `Divides => true` (SelectModulo(3))
        Assert.Equal(expected, VectorEffects.MayThrow(expr));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AnyAndAll_EmptyFlagsAndLongerFlags_ThrowLikeEvaluate()
    {
        using var empty = new GpuBigIntArray(_context, 0, 8);
        using var values = CreateArray(s_countValues);
        using var three = CreateScalar(3);
        IGpuBoolVectorExpression Mismatched() =>
            empty.AsVectorizedEnumerable().IsLessThan(three).And(values.AsVectorizedEnumerable().IsLessThan(three));

        // Flags of 0 and 3 elements are rejected by And as Evaluate rejects them, even though the result would have no
        // element: Any and All answer without a kernel only when the evaluation cannot throw.
        // mutant: BoolVectorQuantifier.Evaluate without the evaluation of a MayThrow source (false / true, no error)
        // mutant: VectorLogicalExpression.MayThrow without `left.Capacity != right.Capacity` (the same)
        using (var flags = Mismatched())
        {
            Assert.Throws<ArgumentException>(() => flags.Evaluate(_context));
        }

        using var any = Mismatched().Any();
        Assert.Throws<ArgumentException>(() => any.Evaluate(_context));
        using var all = Mismatched().All();
        Assert.Throws<ArgumentException>(() => all.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void AnyAndAll_EmptyDivisionByZero_MatchSystemLinq()
    {
        using var empty = new GpuBigIntArray(_context, 0, 8);
        using var zero = CreateScalar(0);

        // A division of no element divides nothing (System.Linq runs no projection), so the evaluation that Any and
        // All now run for a chain that can throw gives false / true; the empty vector launches no kernel.
        // mutant: BoolVectorQuantifier.Evaluate evaluating a MayThrow source without returning for no capacity
        // (CudaException: a grid of 0 blocks)
        using var any = empty.AsVectorizedEnumerable().SelectDivide(zero).IsLessThan(zero).Any();
        Assert.Equal(Array.Empty<BigInteger>().Select(static x => x / 0 < 0).Any(), any.Evaluate(_context));
        using var all = empty.AsVectorizedEnumerable().SelectDivide(zero).IsLessThan(zero).All();
        Assert.Equal(Array.Empty<BigInteger>().Select(static x => x / 0 < 0).All(static b => b), all.Evaluate(_context));
    }

    private static BigInteger SqrtContract(BigInteger x) =>
        x.Sign < 0 ? throw new InvalidOperationException("Cannot compute square root of a negative integer.") : x;
}
