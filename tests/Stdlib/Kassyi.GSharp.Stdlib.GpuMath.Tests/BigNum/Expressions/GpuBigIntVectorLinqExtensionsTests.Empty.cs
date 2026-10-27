namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void EmptyVector_ElementWiseBooleanExpressions_ReturnEmptyResults()
    {
        // And / Or / Not of empty boolean vectors launch nothing (a grid of 0 blocks is an invalid launch) and give
        // empty results, as System.Linq's Zip / Select over empty sequences do.
        // mutant: BoolVectorLogicalExpressions.cs VectorLogicalExpression `if (Length == 0)` -> `if (Length < 0)`
        // input: And / Or (CudaException: invalid value)
        // mutant: BoolVectorLogicalExpressions.cs VectorNotExpression `if (Length == 0)` -> `if (Length < 0)`
        // input: Not
        using var empty = new GpuBigIntArray(_context, 0, 8);
        using var scalarMagnitude = new GpuBigUInt(_context, [5u]);
        var scalar = new GpuBigInt(scalarMagnitude, isNegative: false);
        var reference = Array.Empty<bool>();

        var and = EvaluateBools(empty.AsVectorizedEnumerable().IsLessThan(scalar)
            .And(empty.AsVectorizedEnumerable().IsGreaterThan(scalar)));
        var or = EvaluateBools(empty.AsVectorizedEnumerable().IsLessThan(scalar)
            .Or(empty.AsVectorizedEnumerable().IsGreaterThan(scalar)));
        var not = EvaluateBools(empty.AsVectorizedEnumerable().IsLessThan(scalar).Not());

        Assert.Equal(reference.Zip(reference, static (a, b) => a && b).Count(), and.Length);
        Assert.Equal(reference.Zip(reference, static (a, b) => a || b).Count(), or.Length);
        Assert.Equal(reference.Select(static a => !a).Count(), not.Length);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void EmptyVector_AnyAll_MatchSystemLinq()
    {
        // Any of nothing is false and All of nothing is true, as in System.Linq; neither launches a kernel.
        // mutant: BoolVectorLogicalExpressions.cs VectorAnyTrueExpression `if (source.Length == 0)` -> `if (source.Length < 0)`
        // input: Any (CudaException: invalid value)
        // mutant: BoolVectorLogicalExpressions.cs VectorAllTrueExpression `if (source.Length == 0)` -> `if (source.Length < 0)`
        // input: All
        using var empty = new GpuBigIntArray(_context, 0, 8);
        using var scalarMagnitude = new GpuBigUInt(_context, [5u]);
        var scalar = new GpuBigInt(scalarMagnitude, isNegative: false);
        var reference = Array.Empty<bool>();

        using var any = empty.AsVectorizedEnumerable().IsLessThan(scalar).Any();
        using var all = empty.AsVectorizedEnumerable().IsLessThan(scalar).All();

        Assert.Equal(reference.Any(static b => b), any.Evaluate(_context));
        Assert.Equal(reference.All(static b => b), all.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void EmptyVector_SelectDivideByUInt32_ReturnsEmptyResult()
    {
        // The division by a 32-bit divisor runs a host loop over the elements; an empty vector runs no iteration.
        // The guard is VectorUnaryExpressionBase's `if (evalCount > 0)` (it was already there before #633).
        // mutant: VectorUnaryExpressions.cs VectorUnaryExpressionBase `if (evalCount > 0)` -> `if (evalCount >= 0)`
        // equivalent: the host loop runs no iteration and its copies of 0 words succeed, so this test fixes the result
        // without a mutant that fails it.
        using var empty = new GpuBigIntArray(_context, 0, 8);

        using var expr = empty.AsVectorizedEnumerable().SelectDivideByUInt32(3u);
        var result = expr.Evaluate(_context);

        Assert.Equal(Array.Empty<BigInteger>().Select(static v => v / 3).Count(), result.Length);
    }
}
