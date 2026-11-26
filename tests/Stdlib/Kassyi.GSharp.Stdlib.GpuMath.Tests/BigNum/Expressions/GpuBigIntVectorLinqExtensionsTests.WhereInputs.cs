#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Which inputs of a Where run for an element: the source and the predicate pair up as System.Linq Zip pairs them, and
// nodes outside GpuMath or compound scalars keep their own evaluation. Every expected value or exception type comes
// from the same inputs through System.Linq.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Count_ExternalNodeUnderTakeAfterWhere_ThrowsFromTheWholeEvaluation()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // An external node runs under PendingThrows.Suspend, so its inner evaluation throws immediately instead of
        // recording into the outer Where's space where Take(1) would discard it.
        // mutant: VectorWalk.EvaluateInput without PendingThrows.Suspend on external non-leaf node (returns 1)
        using var external = new PassThroughIntVectorExpression(
            arrA.AsVectorizedEnumerable().ZipDivide(arrB.AsVectorizedEnumerable())
        );
        using var count = external
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1)
            .Count();
        Assert.Throws<DivideByZeroException>(() =>
        {
            using var result = count.Evaluate(_context);
        });
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Count_WhereWhereTakeFewerThanLimit_ThrowsAfterInnerCount()
    {
        BigInteger[] a = [1, 2, 3, 4];
        BigInteger[] b = [1, 1, 1, 0];
        BigInteger[] m1 = [1, 1, 1, 0];
        BigInteger[] m2 = [1, 0, 0, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM1 = CreateArray(m1);
        using var arrM2 = CreateArray(m2);
        using var zero = CreateScalar(0);

        // Inner Where selects n = 3 elements and translates element 3 to index 3 >= n; outer Where has count = 1 < 2,
        // so ReadWord(_count, 0) = 1 stays inside Take(2) and throws whereas returning index 3 would discard it.
        // mutant: WhereDeviceState.TranslateRecorded using `index` instead of `ReadWord(_count, 0)` when `index >= n`
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m1[i] > 0)
            .Where((_, i) => m2[i] > 0)
            .Take(2)
            .Count());
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM1.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Where(arrM2.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(2)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Count_CompoundScalarUnderTakeAfterWhere_Throws()
    {
        BigInteger[] outer = [10, 20, 30];
        BigInteger[] m = [1, 1, 1];
        BigInteger[] innerA = [1, 2];
        BigInteger[] innerB = [1, 0];
        using var arrOuter = CreateArray(outer);
        using var arrM = CreateArray(m);
        using var arrInnerA = CreateArray(innerA);
        using var arrInnerB = CreateArray(innerB);
        using var zero = CreateScalar(0);

        // A compound scalar evaluates under PendingThrows.Suspend so its own PendingThrows scope throws directly
        // instead of recording at index 1 into the outer Where's space where Take(1) would discard it.
        // mutant: VectorEffects.EvaluateScalar without PendingThrows.Suspend (returns 1)
        using var scalarExpr = arrInnerA.AsVectorizedEnumerable()
            .ZipDivide(arrInnerB.AsVectorizedEnumerable())
            .Count();
        using var count = new VectorAddScalarExpression(arrOuter.AsVectorizedEnumerable(), scalarExpr.Node)
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1)
            .Count();
        Assert.Throws<DivideByZeroException>(() =>
        {
            using var result = count.Evaluate(_context);
        });
    }

    // Divisors of the outer predicate: a zero at index 0 runs (inside the 1 element the inner Where selects); a zero
    // at index 1 or 2 is past the end of the source, where Zip stops before the predicate.
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary | TestScenario.SadPath)]
    [InlineData(new long[] { 0, 1, 1 })]
    [InlineData(new long[] { 1, 0, 1 })]
    [InlineData(new long[] { 1, 1, 0 })]
    public void Count_WhereWherePredicatePastInnerCount_CountsAsZip(long[] divisors)
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 1, 1];
        BigInteger[] m = [1, 0, 0];
        BigInteger[] c = [1, 1, 1];
        var d = divisors.Select(static x => (BigInteger)x).ToArray();
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var arrC = CreateArray(c);
        using var arrD = CreateArray(d);
        using var zero = CreateScalar(0);

        // The inner Where selects 1 element on the device; the outer predicate has 3 elements.
        // mutant: WhereDeviceState.RecordTranslated without dropping `fromPredicate.Index >= sourceLength` (throws)
        // mutant: WhereDeviceState.RecordTranslated with `fromPredicate.Index > sourceLength` (throws for { 1, 0, 1 })
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Zip(Enumerable.Range(0, c.Length).Select(i => c[i] / d[i] > 0))
            .Where(static t => t.Second)
            .Count());

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Where(arrC.AsVectorizedEnumerable().ZipDivide(arrD.AsVectorizedEnumerable()).IsGreaterThan(zero))
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    // Divisors of the source: a zero at index 1 runs (Zip evaluates the source at the predicate's length before it
    // finds the predicate ended); a zero at index 2 is past it.
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary | TestScenario.SadPath)]
    [InlineData(new long[] { 1, 0, 1 })]
    [InlineData(new long[] { 1, 1, 0 })]
    public void Count_WhereSourcePastDevicePredicateCount_CountsAsZip(long[] divisors)
    {
        BigInteger[] a = [1, 2, 3];
        var b = divisors.Select(static x => (BigInteger)x).ToArray();
        BigInteger[] y = [1, 1, 1];
        BigInteger[] m = [1, 0, 0];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrY = CreateArray(y);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // The predicate has 1 element on the device (a Where of y); the source has 3.
        // mutant: WhereDeviceState.RecordTranslated without dropping `fromSource.Index > predicateLength` (throws)
        // mutant: WhereDeviceState.RecordTranslated with `fromSource.Index >= predicateLength`
        // (returns for { 1, 0, 1 })
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Zip(Enumerable.Range(0, y.Length).Where(i => m[i] > 0).Select(i => y[i] > 0))
            .Where(static t => t.Second)
            .Count());

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrY.AsVectorizedEnumerable()
                .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
                .IsGreaterThan(zero))
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void EvaluateOnCallStack_TakeAfterWhereThrowAfterWindow_ReturnsWindow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // EvaluateOnCallStack keeps the element exceptions of Evaluate: the zero divisor is past Take(1).
        // mutant: VectorWalk.EvaluateBounded calling EvaluateDirect without EvaluateRecording (throws)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(1).ToArray()
        );
        Assert.Null(expected.Error);

        using var expr = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1);
        var actual = Outcome(() => ToHostArray(expr.EvaluateOnCallStack(_context)));
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.Value, actual.Value);
    }

    private int EvaluateCount(GpuBigIntExpr countExpr)
    {
        using var result = countExpr.Evaluate(_context);
        return (int)ToBigInt(result);
    }

    // An integer vector implemented outside GpuMath: evaluates the inner expression inside its own Evaluate
    private sealed class PassThroughIntVectorExpression(IGpuBigIntVectorExpression inner) : IGpuBigIntVectorExpression
    {
        public int Length => inner.Length;

        public int Capacity => inner.Capacity;

        public bool TryGetLength(out int length) => inner.TryGetLength(out length);

        public int WordsPerElement => inner.WordsPerElement;

        public long BitsPerElement => inner.BitsPerElement;

        public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigUIntArrayView targetResult = default) =>
            inner.Evaluate(workspace, elementOffset, elementCount, targetResult);

        public void Dispose() => inner.Dispose();
    }
}
