namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    // The elements of every case; a divisor vector with a zero in the middle, and one without
    private static readonly BigInteger[] s_countValues = [6, 9, 12];
    private static readonly BigInteger[] s_zeroInTheMiddle = [1, 0, 3];
    private static readonly BigInteger[] s_noZero = [1, 2, 3];

    // Each chain with its GPU expression, its System.Linq reference over the same elements (BigInteger has no square
    // root; the contract of SelectSqrt, InvalidOperationException for a negative element, stands in) and whether a
    // node of it can throw
    private static readonly CountCase[] s_countCases =
    [
        new("leaf", static o => o.Values, static () => s_countValues.Length, false),
        new(
            "SelectAdd(3).SelectSubtract(3).SelectMultiply(3)",
            static o => o.Values.SelectAdd(o.Three).SelectSubtract(o.Three).SelectMultiply(o.Three),
            static () => s_countValues.Select(static x => (x + 3 - 3) * 3).Count(),
            false
        ),
        new(
            "Take(2).Skip(1)",
            static o => o.Values.Take(2).Skip(1),
            static () => s_countValues.Take(2).Skip(1).Count(),
            false
        ),
        new(
            "ZipAdd(no zero).ZipMultiply(no zero)",
            static o => o.Values.ZipAdd(o.NoZero).ZipMultiply(o.NoZero),
            static () => s_countValues
                .Zip(s_noZero, static (a, b) => a + b)
                .Zip(s_noZero, static (a, b) => a * b)
                .Count(),
            false
        ),
        new(
            "SelectDivideByUInt32(2)",
            static o => o.Values.SelectDivideByUInt32(2),
            static () => s_countValues.Select(static x => x / 2u).Count(),
            false
        ),
        new(
            "SelectDivide(0)",
            static o => o.Values.SelectDivide(o.Zero),
            static () => s_countValues.Select(static x => x / 0).Count(),
            true
        ),
        new(
            "SelectModulo(0)",
            static o => o.Values.SelectModulo(o.Zero),
            static () => s_countValues.Select(static x => x % 0).Count(),
            true
        ),
        new(
            "SelectDivideByUInt32(0)",
            static o => o.Values.SelectDivideByUInt32(0),
            static () => s_countValues.Select(static x => x / 0u).Count(),
            true
        ),
        new(
            "SelectDivide(0).Take(2)",
            static o => o.Values.SelectDivide(o.Zero).Take(2),
            static () => s_countValues.Select(static x => x / 0).Take(2).Count(),
            true
        ),
        new(
            "SelectDivide(0).Skip(3)",
            static o => o.Values.SelectDivide(o.Zero).Skip(3),
            static () => s_countValues.Select(static x => x / 0).Skip(3).Count(),
            true
        ),
        new(
            "SelectDivide(0).Take(0)",
            static o => o.Values.SelectDivide(o.Zero).Take(0),
            static () => s_countValues.Select(static x => x / 0).Take(0).Count(),
            true
        ),
        new(
            "SelectDivide(0).SelectAdd(3)",
            static o => o.Values.SelectDivide(o.Zero).SelectAdd(o.Three),
            static () => s_countValues.Select(static x => x / 0).Select(static x => x + 3).Count(),
            true
        ),
        new(
            "ZipAdd(SelectDivide(0))",
            static o => o.Values.ZipAdd(o.OtherValues.SelectDivide(o.Zero)),
            static () => s_countValues.Zip(s_countValues.Select(static x => x / 0), static (a, b) => a + b).Count(),
            true
        ),
        new(
            "ZipDivide(zero in the middle)",
            static o => o.Values.ZipDivide(o.ZeroInTheMiddle),
            static () => s_countValues.Zip(s_zeroInTheMiddle, static (a, b) => a / b).Count(),
            true
        ),
        new(
            "ZipModulo(zero in the middle)",
            static o => o.Values.ZipModulo(o.ZeroInTheMiddle),
            static () => s_countValues.Zip(s_zeroInTheMiddle, static (a, b) => a % b).Count(),
            true
        ),
        new(
            "SelectMultiply(-1).SelectSqrt()",
            static o => o.Values.SelectMultiply(o.MinusOne).SelectSqrt(),
            static () => s_countValues.Select(static x => -x).Select(SqrtContract).Count(),
            true
        ),
        new(
            "SelectSqrt()",
            static o => o.Values.SelectSqrt(),
            static () => s_countValues.Select(SqrtContract).Count(),
            true
        ),
        new(
            "SelectDivide(3)",
            static o => o.Values.SelectDivide(o.Three),
            static () => s_countValues.Select(static x => x / 3).Count(),
            true
        ),
        new(
            "SelectModulo(3)",
            static o => o.Values.SelectModulo(o.Three),
            static () => s_countValues.Select(static x => x % 3).Count(),
            true
        ),
        new(
            "ZipDivide(no zero)",
            static o => o.Values.ZipDivide(o.NoZero),
            static () => s_countValues.Zip(s_noZero, static (a, b) => a / b).Count(),
            true
        ),
        new(
            "SelectDivide(3).Take(1)",
            static o => o.Values.SelectDivide(o.Three).Take(1),
            static () => s_countValues.Select(static x => x / 3).Take(1).Count(),
            true
        ),
        new(
            "SelectDivide(3).SelectAdd(3)",
            static o => o.Values.SelectDivide(o.Three).SelectAdd(o.Three),
            static () => s_countValues.Select(static x => x / 3 + 3).Count(),
            true
        ),
        new(
            "ZipAdd(no zero.SelectDivide(3))",
            static o => o.Values.ZipAdd(o.NoZero.SelectDivide(o.Three)),
            static () => s_countValues.Zip(s_noZero.Select(static x => x / 3), static (a, b) => a + b).Count(),
            true
        ),
    ];

    public static TheoryData<CountCase> CountCases => [.. s_countCases];

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(CountCases))]
    public void Count_Chain_AgreesWithSystemLinqAndEvaluate(CountCase chain)
    {
        using var operands = CountOperandArrays.Create(this);

        var expectedCount = 0;
        var expectedError = Record.Exception(() => expectedCount = chain.Reference());

        var count = -1;
        var countError = Record.Exception(() =>
        {
            using var countExpr = chain.Build(operands.Operands()).Count();
            using var result = countExpr.Evaluate(_context);
            count = (int)ToBigInt(result);
        });

        var length = -1;
        var evaluateError = Record.Exception(() =>
        {
            using var expr = chain.Build(operands.Operands());
            length = expr.Evaluate(_context).Length;
        });

        // Count throws what System.Linq throws, and what Evaluate of the same chain throws: the result of a chain does
        // not depend on the operator that ends it.
        // mutant: VectorCountExpression.Evaluate without DeviceCounted.EvaluateIfMayThrow (every throwing row)
        // mutant: VectorBinaryScalarExpressionBase.MayThrow without `ElementMayThrow` (SelectDivide(0) returns 3)
        // mutant: VectorSqrtExpression.ElementMayThrow => false (SelectMultiply(-1).SelectSqrt() returns 3)
        // mutant: VectorDivByUInt32Expression.ElementMayThrow => false (SelectDivideByUInt32(0) returns 3)
        // mutant: the Zip ElementMayThrow without ChecksZeroDivisor (ZipDivide(zero in the middle) returns 3)
        // mutant: VectorTakeExpression.MayThrow => false (SelectDivide(0).Take(2) returns 2)
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

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(CountCases))]
    public void MayThrow_Chain_IsDecidedPerNode(CountCase chain)
    {
        using var operands = CountOperandArrays.Create(this);
        using var expr = chain.Build(operands.Operands());

        // Count answers from the length only for a chain of plain operations (no evaluation, no synchronization); a
        // chain with an operation that can throw is evaluated, whatever its values. The decision reads no value.
        // mutant: VectorEffects.MayThrow returning true for an array (leaf and every plain chain)
        // mutant: VectorDivByUInt32Expression.ElementMayThrow => true (SelectDivideByUInt32(2))
        // mutant: VectorModScalarExpression without `ElementMayThrow => true` (SelectModulo(3))
        Assert.Equal(chain.MayThrow, VectorEffects.MayThrow(expr));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void MayThrow_ZipOfDifferentLengths_IsTrue()
    {
        using var values = CreateArray(s_countValues);
        using var shorter = CreateArray([1, 2]);
        using var expr = values.AsVectorizedEnumerable().ZipAdd(shorter.AsVectorizedEnumerable());

        // A Zip of different capacities throws when it is evaluated, so Count must evaluate it.
        // mutant: the Zip MayThrow without `Capacity != _rightCapacity`
        Assert.True(VectorEffects.MayThrow(expr));
    }

    [SkippableFact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void CountInAGraph_ChainWithoutThrowingOperation_IsCaptured()
    {
        using var values = CreateArray(s_countValues);
        using var three = CreateScalar(3);
        var count = values.AsVectorizedEnumerable().SelectAdd(three).Count();

        // A chain that cannot throw is not evaluated: its host length is written inside the graph, as before.
        // mutant: DeviceCounted.EvaluateIfMayThrow refusing every capture (NotSupportedException)
        using var graph = count.CompileToGraph(_context);
        graph.Execute();

        Assert.Equal(new BigInteger(s_countValues.Length), graph.Result.Value.ToBigInteger());
    }

    public static TheoryData<string> ThrowingChainsInAGraph => ["SelectDivide(3)", "SelectSqrt()"];

    [SkippableTheory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(ThrowingChainsInAGraph))]
    public void CountInAGraph_ChainThatCanThrow_IsRefusedBeforeAnyWork(string chain)
    {
        using var values = CreateArray(s_countValues);
        using var three = CreateScalar(3);
        var vector = values.AsVectorizedEnumerable();
        var count = (chain == "SelectDivide(3)" ? vector.SelectDivide(three) : vector.SelectSqrt()).Count();

        // The checks of the chain read the device, which a capture cannot record: one NotSupportedException before any
        // work (the square root would otherwise invalidate the capture with a raw CUDA error), and the workspace still
        // counts the same chain without a graph.
        // mutant: DeviceCounted.EvaluateIfMayThrow without ThrowIfCapturingChecks (SelectSqrt(): AggregateException
        // with CudaErrorStreamCaptureUnsupported; SelectDivide(3): the NotSupportedException of the scalar zero test)
        var error = Assert.Throws<NotSupportedException>(() => count.CompileToGraph(_context));
        Assert.StartsWith("The check of a division, a square root or the lengths of a counted vector", error.Message);

        using var again = values.AsVectorizedEnumerable().SelectSqrt().Count();
        using var result = again.Evaluate(_context);
        Assert.Equal(new BigInteger(s_countValues.Length), ToBigInt(result));
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
        // mutant: BoolVectorQuantifier.EvaluateEmpty without DeviceCounted.EvaluateIfMayThrow (false / true, no error)
        // mutant: VectorLogicalExpression.MayThrow without `Capacity != _rightCapacity` (the same)
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
        // mutant: BoolVectorQuantifier.Evaluate evaluating a MayThrow source of no capacity as a non-empty one
        // (CudaException: a grid of 0 blocks)
        using var any = empty.AsVectorizedEnumerable().SelectDivide(zero).IsLessThan(zero).Any();
        Assert.Equal(Array.Empty<BigInteger>().Select(static x => x / 0 < 0).Any(), any.Evaluate(_context));
        using var all = empty.AsVectorizedEnumerable().SelectDivide(zero).IsLessThan(zero).All();
        Assert.Equal(
            Array.Empty<BigInteger>().Select(static x => x / 0 < 0).All(static b => b),
            all.Evaluate(_context)
        );
    }

    private static BigInteger SqrtContract(BigInteger x) =>
        x.Sign < 0 ? throw new InvalidOperationException("Cannot compute square root of a negative integer.") : x;

    /// <summary>A counted chain: its name, its GPU chain, its System.Linq count and whether it can throw.</summary>
    public sealed record CountCase(
        string Name,
        Func<CountOperands, IGpuBigIntVectorExpression> Build,
        Func<int> Reference,
        bool MayThrow)
    {
        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>The inputs of a counted chain, as fresh expressions over the arrays of one test.</summary>
    public sealed record CountOperands(
        IGpuBigIntVectorExpression Values,
        IGpuBigIntVectorExpression OtherValues,
        IGpuBigIntVectorExpression ZeroInTheMiddle,
        IGpuBigIntVectorExpression NoZero,
        GpuBigInt Zero,
        GpuBigInt Three,
        GpuBigInt MinusOne);

    // The arrays and scalars of one test; Operands() gives fresh leaves over them for each chain
    private sealed class CountOperandArrays : IDisposable
    {
        private readonly GpuBigIntArray _values;
        private readonly GpuBigIntArray _zeroInTheMiddle;
        private readonly GpuBigIntArray _noZero;
        private readonly GpuBigInt _zero;
        private readonly GpuBigInt _three;
        private readonly GpuBigInt _minusOne;

        private CountOperandArrays(GpuBigIntVectorLinqExtensionsTests tests)
        {
            _values = tests.CreateArray(s_countValues);
            _zeroInTheMiddle = tests.CreateArray(s_zeroInTheMiddle);
            _noZero = tests.CreateArray(s_noZero);
            _zero = tests.CreateScalar(0);
            _three = tests.CreateScalar(3);
            _minusOne = tests.CreateScalar(-1);
        }

        public static CountOperandArrays Create(GpuBigIntVectorLinqExtensionsTests tests) => new(tests);

        public CountOperands Operands() => new(
            _values.AsVectorizedEnumerable(),
            _values.AsVectorizedEnumerable(),
            _zeroInTheMiddle.AsVectorizedEnumerable(),
            _noZero.AsVectorizedEnumerable(),
            _zero,
            _three,
            _minusOne);

        public void Dispose()
        {
            _values.Dispose();
            _zeroInTheMiddle.Dispose();
            _noZero.Dispose();
            _zero.Dispose();
            _three.Dispose();
            _minusOne.Dispose();
        }
    }
}
