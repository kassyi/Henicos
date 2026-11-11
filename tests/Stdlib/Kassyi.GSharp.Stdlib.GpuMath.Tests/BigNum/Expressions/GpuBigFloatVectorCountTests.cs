namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>Count of floating-point vector chains, with and without a workspace, against System.Linq.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatVectorCountTests : GpuBigFloatWorkspaceTestBase
{
    // The elements of every float Count case
    private static readonly long[] s_floatCountValues = [6, -4, 6];

    // Each chain with its GPU expression and its System.Linq count over the same elements: decimal stands in for the
    // division, since, like GpuBigFloat and unlike double, it has no infinity; the contract of SelectSqrt
    // (InvalidOperationException for a negative element) stands in for the square root
    private static readonly FloatCountCase[] s_floatCountCases =
    [
        new(
            "SelectDivide(0)",
            static o => o.Values.SelectDivide(o.Zero),
            static () => s_floatCountValues.Select(static x => (decimal)x / 0m).Count()
        ),
        new(
            "SelectSqrt()",
            static o => o.Values.SelectSqrt(),
            static () => s_floatCountValues.Select(FloatSqrtContract).Count()
        ),
        new(
            "SelectSqrt().Take(1)",
            static o => o.Values.SelectSqrt().Take(1),
            static () => s_floatCountValues.Select(FloatSqrtContract).Take(1).Count()
        ),
        new(
            "SelectSqrt().Take(0)",
            static o => o.Values.SelectSqrt().Take(0),
            static () => s_floatCountValues.Select(FloatSqrtContract).Take(0).Count()
        ),
        new(
            "SelectDivide(3)",
            static o => o.Values.SelectDivide(o.Three),
            static () => s_floatCountValues.Select(static x => x / 3m).Count()
        ),
        new(
            "SelectAdd(3).SelectMultiply(3).Skip(1)",
            static o => o.Values.SelectAdd(o.Three).SelectMultiply(o.Three).Skip(1),
            static () => s_floatCountValues.Select(static x => (x + 3) * 3).Skip(1).Count()
        ),
        new(
            "Where(x > 0)",
            static o => o.Values.Where(o.OtherValues.IsGreaterThan(o.Zero)),
            static () => s_floatCountValues.Where(static x => x > 0).Count()
        ),
    ];

    public static TheoryData<FloatCountCase> FloatCountCases => [.. s_floatCountCases];

    // Both Count overloads give the System.Linq answer: the exception of the first counted element whose projection
    // throws, otherwise the number of elements. Count() takes the workspace of the chain's arrays when it must
    // evaluate.
    // mutant: GpuBigFloatVectorLinqExtensions.Count(workspace) without DeviceCounted.EvaluateIfMayThrow
    // (SelectDivide(0), SelectSqrt() return 3)
    // mutant: Count() returning source.Length without evaluating (the same rows)
    // mutant: FloatVectorDivScalarExpression without `ElementMayThrow => true` (SelectDivide(0) returns 3)
    // mutant: FloatVectorUnaryExpression.ElementMayThrow => false (SelectSqrt() returns 3)
    // mutant: FloatVectorTakeExpression.OperandRequest asking for the whole capacity of its input (SelectSqrt().Take(1)
    // throws)
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(FloatCountCases))]
    public void Count_Chain_AgreesWithSystemLinq(FloatCountCase chain)
    {
        using var values = FloatCountArray();
        using var zero = CreateFloat(0);
        using var three = CreateFloat(3);
        FloatCountOperands Operands() =>
            new(values.AsVectorizedEnumerable(), values.AsVectorizedEnumerable(), zero, three);

        var expected = 0;
        var expectedError = Record.Exception(() => expected = chain.Reference());

        var withWorkspace = -1;
        var withWorkspaceError = Record.Exception(() =>
        {
            using var expr = chain.Build(Operands());
            withWorkspace = expr.Count(Context);
        });

        var withoutWorkspace = -1;
        var withoutWorkspaceError = Record.Exception(() =>
        {
            using var expr = chain.Build(Operands());
            withoutWorkspace = expr.Count();
        });

        Assert.Equal(expectedError?.GetType(), withWorkspaceError?.GetType());
        Assert.Equal(expectedError?.GetType(), withoutWorkspaceError?.GetType());
        if (expectedError is null)
        {
            Assert.Equal(expected, withWorkspace);
            Assert.Equal(expected, withoutWorkspace);
        }
    }

    // An expression implemented outside GpuMath may throw, so counting it evaluates it, and it holds no array whose
    // workspace Count() could use: Count() refuses it, Count(workspace) counts it.
    // mutant: the ArrayWorkspace walk returning the workspace of the first array of any kind (a NullReferenceException
    // or a count without the refusal)
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Count_ExpressionWithoutGpuMathArray_RefusesWithoutAWorkspace()
    {
        using var values = FloatCountArray();
        using var outside = new OutsideFloatVector(values.AsVectorizedEnumerable());

        Assert.Throws<InvalidOperationException>(() => outside.Count());
        Assert.Equal(s_floatCountValues.Length, outside.Count(Context));
    }

    private GpuBigFloatArray FloatCountArray()
    {
        var array = new GpuBigFloatArray(Context, s_floatCountValues.Length, 4);
        for (var i = 0; i < s_floatCountValues.Length; i++)
        {
            using var element = CreateFloat(s_floatCountValues[i]);
            array.SetElement(i, element);
        }

        return array;
    }

    private static decimal FloatSqrtContract(long x) =>
        x < 0 ? throw new InvalidOperationException("Cannot compute square root of a negative number.") : x;

    /// <summary>A counted float chain: its name, its GPU expression and its System.Linq count.</summary>
    public sealed record FloatCountCase(
        string Name,
        Func<FloatCountOperands, IGpuBigFloatVectorExpression> Build,
        Func<int> Reference)
    {
        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>The inputs of a counted float chain.</summary>
    public sealed record FloatCountOperands(
        IGpuBigFloatVectorExpression Values,
        IGpuBigFloatVectorExpression OtherValues,
        GpuBigFloat Zero,
        GpuBigFloat Three);

    // A float vector implemented outside GpuMath, over a GpuMath one
    private sealed class OutsideFloatVector(IGpuBigFloatVectorExpression inner) : IGpuBigFloatVectorExpression
    {
        public int Length => inner.Length;

        public int WordsPerElement => inner.WordsPerElement;

        public GpuBigFloatArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigFloatArrayView targetResult = default) =>
            inner.Evaluate(workspace, elementOffset, elementCount, targetResult);

        public void Dispose() => inner.Dispose();
    }
}
