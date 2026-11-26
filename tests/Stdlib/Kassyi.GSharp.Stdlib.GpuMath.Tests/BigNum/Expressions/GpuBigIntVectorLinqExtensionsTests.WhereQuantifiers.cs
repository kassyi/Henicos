#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All through Where stop at the deciding element, as System.Linq does: an element after it never runs its
// projections. Every expected value or exception type comes from the same inputs through System.Linq.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Any_ThroughWhereDecideBeforeThrow_ReturnsTrue()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 1, 0];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Element 2 divides by zero after selected element 0 already decides Any.
        // mutant: BoolVectorQuantifier.CanDefer rejecting Where (throws DivideByZeroException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Any(static v => v > 0));
        Assert.Equal((true, null), expected);

        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void All_ThroughWhereDecideBeforeThrow_ReturnsFalse()
    {
        BigInteger[] a = [0, 2, 3];
        BigInteger[] b = [1, 1, 0];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Element 2 divides by zero after selected element 0 already decides All.
        // mutant: BoolVectorQuantifier.CanDefer rejecting Where (throws DivideByZeroException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .All(static v => v > 0));
        Assert.Equal((false, null), expected);

        using var all = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(zero)
            .All();
        Assert.Equal(expected, Outcome(() => all.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Any_ThroughWhereUnselectedThrowBeforeDecide_Throws()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 1];
        BigInteger[] m = [1, 0, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);
        using var two = CreateScalar(2);

        // Element 1 is unselected, yet its projection runs before selected output 1 (element 2)
        // decides (T_out = D = 1).
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `<` (returns true)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Any(static v => v > 2));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(two)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Any_ThroughWhereSkip_ThrowsForSkipped()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [0, 1, 1];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Skip(1) after Where still pulls element 0 before yielding element 1, so element 0 throws (clamped to 0).
        // mutant: WhereDeviceState.TranslateRecorded returning null when `j < 0` instead of clamping to 0
        // (returns true)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Skip(1)
            .Any(static v => v > 0));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Skip(1)
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Any_ThroughWhereNoMatch_ThrowsIfAnyThrow()
    {
        BigInteger[] a = [1, 2];
        BigInteger[] b = [1, 0];
        BigInteger[] m = [0, 0];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // With zero matching elements, Any enumerates all inputs and throws element 1's failure.
        // mutant: BoolVectorQuantifier.Evaluate ignoring pending.Exception when `decidedIndex == uint.MaxValue`
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Any(static v => v > 0));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Any_ThroughWhereDownstreamThrow_PicksCSharpOrder()
    {
        BigInteger[] a = [4, -4, 9];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);
        using var one = CreateScalar(1);

        // Downstream SelectSqrt throws at selected index 1 after selected index 0 (sqrt(4) = 2 > 1) decides Any.
        // mutant: BoolVectorQuantifier.CanDefer rejecting Where (throws InvalidOperationException)
        // mutant: VectorSqrtExpression throwing instead of recording (throws InvalidOperationException)
        var expected = Outcome(() => a
            .Where((_, i) => m[i] > 0)
            .Select(IsqrtContract)
            .Any(static v => v > 1));
        Assert.Equal((true, null), expected);

        using var any = arrA.AsVectorizedEnumerable()
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .SelectSqrt()
            .IsGreaterThan(one)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Any_ThroughWhereManyBlocks_PicksLowest()
    {
        BigInteger[] a = ManyBlockValues(static i => i == 4000 ? 1 : 0);
        BigInteger[] b = ManyBlockValues(static i => i == 4001 ? 0 : 1);
        BigInteger[] m = ManyBlockValues(static i => (i & 1) == 0 ? 1 : 0);
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Across multi-block compaction, element 4000 maps to output 2000 (decides) before element 4001 (scan = 2001).
        // mutant: BoolVectorQuantifier.CanDefer rejecting Where (throws DivideByZeroException)
        var expected = Outcome(() => Enumerable.Range(0, ManyBlocks)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Any(static v => v > 0));
        Assert.Equal((true, null), expected);

        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }
}
