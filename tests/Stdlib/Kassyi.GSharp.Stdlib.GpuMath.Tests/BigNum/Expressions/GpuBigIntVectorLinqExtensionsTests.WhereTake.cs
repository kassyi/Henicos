#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Take after Where runs source and predicate projections only up to the k-th selected element (Skip after Where runs
// them for skipped elements as well), matching System.Linq. Every expected value or exception type comes from the same
// inputs through System.Linq.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Count_TakeAfterWhereThrowAfterWindow_CountsWindow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Element 1 divides by zero after the 1st selected element (element 0), outside Take(1).
        // mutant: WhereDeviceState.TranslateRecorded without `j >= evalCount` check (throws DivideByZeroException)
        // mutant: WhereDeviceState.TranslateRecorded with `j > evalCount` instead of `j >= evalCount` (throws)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws DivideByZeroException)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(1).Count()
        );
        Assert.Equal((1, null), expected);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Sum_TakeAfterWhereThrowAfterWindow_SumsWindow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Sum over Take(1) after Where evaluates through VectorWalk and discards failures past the window.
        // mutant: WhereDeviceState.TranslateRecorded without `j >= evalCount` check (throws DivideByZeroException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws DivideByZeroException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Take(1)
            .Aggregate(BigInteger.Zero, static (s, v) => s + v));
        Assert.Equal((BigInteger.One, null), expected);

        using var sumExpr = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1)
            .Sum();
        var actual = Outcome(() =>
        {
            using var sum = sumExpr.Evaluate(_context);
            return ToBigInt(sum);
        });
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Evaluate_TakeAfterWhereThrowAfterWindow_ReturnsWindow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Direct Evaluate of Take(1) after Where returns only the first selected element despite a later zero divisor.
        // mutant: WhereDeviceState.TranslateRecorded without `j >= evalCount` check (throws DivideByZeroException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws DivideByZeroException)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(1).ToArray()
        );
        Assert.Null(expected.Error);
        Assert.NotNull(expected.Value);
        Assert.Equal([BigInteger.One], expected.Value);

        using var expr = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1);
        var actual = Outcome(() => ToHostArray(expr.Evaluate(_context)));
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.Value, actual.Value);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Count_FloatTakeAfterWhereThrowAfterWindow_CountsWindow()
    {
        long[] a = [1, 2, 3];
        long[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateFloatArray(a);
        using var arrB = CreateFloatArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // FloatVectorWhereExpression translates operand exceptions identically to VectorWhereExpression.
        // mutant: FloatVectorWhereExpression without RecordTranslated (throws DivideByZeroException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws DivideByZeroException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => b[i] == 0 ? throw new DivideByZeroException() : a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Take(1)
            .Count());
        Assert.Equal((1, null), expected);

        using var filtered = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1);
        Assert.Equal(expected, Outcome(() => filtered.Count(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Count_FloatTakeAfterWherePredicateThrowsAfterWindow_CountsWindow()
    {
        long[] a = [1, 2, 3];
        long[] b = [1, 1, 1];
        BigInteger[] p = [1, 1, -4];
        using var arrA = CreateFloatArray(a);
        using var arrB = CreateFloatArray(b);
        using var arrP = CreateArray(p);
        using var zero = CreateScalar(0);

        // The predicate square root of the float Where fails at element 2 after element 0 already filled Take(1).
        // mutant: FloatVectorWhereExpression.EvaluateDirect without BeginOperand(0) (InvalidOperationException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => b[i] == 0 ? throw new DivideByZeroException() : a[i] / b[i])
            .Zip(p.Select(IsqrtContract))
            .Where(static t => t.Second > 0)
            .Take(1)
            .Count());
        Assert.Equal((1, null), expected);

        using var filtered = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrP.AsVectorizedEnumerable().SelectSqrt().IsGreaterThan(zero))
            .Take(1);
        Assert.Equal(expected, Outcome(() => filtered.Count(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Count_TakeAfterWherePredicateThrowsAfterWindow_CountsWindow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 1, 1];
        BigInteger[] p = [1, 1, -4];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrP = CreateArray(p);
        using var zero = CreateScalar(0);

        // Predicate square root fails at element 2 after element 0 already filled Take(1).
        // mutant: VectorWhereExpression.BeginOperand skipping OpenSpace on predicate operand
        // (InvalidOperationException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws InvalidOperationException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Zip(p.Select(IsqrtContract))
            .Where(static t => t.Second > 0)
            .Take(1)
            .Count());
        Assert.Equal((1, null), expected);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrP.AsVectorizedEnumerable().SelectSqrt().IsGreaterThan(zero))
            .Take(1)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Count_WherePredicateThrowsAfterSource_ThrowsSource()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [0, 1, 1];
        BigInteger[] p = [1, 1, -4];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrP = CreateArray(p);
        using var zero = CreateScalar(0);

        // Source fails at index 0 before predicate fails at index 2, even though predicate runs first on the GPU.
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (InvalidOperationException from predicate)
        // mutant: VectorWhereExpression.BeginOperand skipping OpenSpace on predicate (InvalidOperationException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Zip(p.Select(IsqrtContract))
            .Where(static t => t.Second > 0)
            .Count());
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrP.AsVectorizedEnumerable().SelectSqrt().IsGreaterThan(zero))
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Count_WhereSourceAndPredicateThrowAtOneIndex_ThrowsSource()
    {
        BigInteger[] a = [1, 2];
        BigInteger[] b = [1, 0];
        BigInteger[] p = [1, -4];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrP = CreateArray(p);
        using var zero = CreateScalar(0);

        // At element 1, System.Linq evaluates the source projection before the predicate.
        // mutant: WhereDeviceState.RecordTranslated using `<` instead of `<=` on source index
        // (InvalidOperationException)
        // mutant: VectorWhereExpression.BeginOperand skipping OpenSpace on predicate (InvalidOperationException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Zip(p.Select(IsqrtContract))
            .Where(static t => t.Second > 0)
            .Count());
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrP.AsVectorizedEnumerable().SelectSqrt().IsGreaterThan(zero))
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.Boundary)]
    public void Count_WhereWhereTake_CountsWindow()
    {
        BigInteger[] a = [1, 2, 3, 4];
        BigInteger[] b = [1, 1, 1, 0];
        BigInteger[] m = [1, 1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Nested Where preserves exception index translation across multiple compaction stages up to Take(2).
        // mutant: WhereDeviceState.TranslateRecorded without `j >= evalCount` check (throws DivideByZeroException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws DivideByZeroException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Where((_, i) => m[i] > 0)
            .Take(2)
            .Count());
        Assert.Equal((2, null), expected);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(2)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Count_DeepChainAboveTakeAfterWhere_CountsWindow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // 20 additions above Take(1) push Where past RecursionLimit onto ExpressionWalk so Walker.BeginOperand runs.
        // mutant: VectorWalk.Walker.BeginOperand left empty (throws DivideByZeroException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws DivideByZeroException)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(1).Count()
        );
        Assert.Equal((1, null), expected);

        var chain = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(1);
        for (var i = 0; i < 20; i++)
        {
            chain = chain.SelectAdd(zero);
        }

        using var count = chain.Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Count_DeepChainBetweenSourceAndWhere_PredicateOrderKept()
    {
        BigInteger[] a = [1, 2];
        BigInteger[] b = [1, 0];
        BigInteger[] p = [1, -4];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrP = CreateArray(p);
        using var zero = CreateScalar(0);

        // Deep source below Where exercises ExpressionWalk inside an open PendingThrows space.
        // mutant: WhereDeviceState.RecordTranslated using `<` instead of `<=` (throws InvalidOperationException)
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (throws InvalidOperationException)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Zip(p.Select(IsqrtContract))
            .Where(static t => t.Second > 0)
            .Count());
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        var source = arrA.AsVectorizedEnumerable().ZipDivide(arrB.AsVectorizedEnumerable());
        for (var i = 0; i < 20; i++)
        {
            source = source.SelectAdd(zero);
        }

        using var count = source.Where(arrP.AsVectorizedEnumerable().SelectSqrt().IsGreaterThan(zero)).Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Count_SiblingsThrowAtDifferentIndices_ThrowsLowerIndex()
    {
        BigInteger[] a = [1, 1, -4];
        BigInteger[] b = [1, 1, 1];
        BigInteger[] c = [0, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrC = CreateArray(c);

        // Right sibling throws at index 0 while left sibling throws at index 2; lowest element index wins.
        // mutant: VectorWalk.Evaluate without EvaluateRecording scope (InvalidOperationException from left sibling)
        var expected = Outcome(() => a.Select(IsqrtContract)
            .Zip(Enumerable.Range(0, b.Length).Select(i => b[i] / c[i]), static (l, r) => l + r)
            .Count());
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .SelectSqrt()
            .ZipAdd(arrB.AsVectorizedEnumerable().ZipDivide(arrC.AsVectorizedEnumerable()))
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Count_TakeAfterWhereThrowAtKthMatch_Throws()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Element 1 is the 2nd selected element (index 1 < Take(2)), so the failure at index 1 must be thrown.
        // mutant: VectorWalk.EvaluateRecording without ThrowRecorded(exception) at end of scope (returns 2)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(2).Count()
        );
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(2)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Count_TakeAfterWhereFewerMatches_ThrowsAfterLastMatch()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 1, 0];
        BigInteger[] m = [1, 0, 0];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Only 1 element matches while Take(2) demands 2, so Take pulls element 2 (scan[2] = 1 < 2) and throws.
        // mutant: WhereDeviceState.TranslateRecorded using `index` instead of `ReadWord(scan, index)` (returns 1)
        // mutant: VectorWalk.EvaluateRecording without ThrowRecorded(exception) at end of scope (returns 1)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(2).Count()
        );
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(2)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Count_TakeAfterWhereThrowInUnselectedBeforeKth_Throws()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        BigInteger[] m = [1, 0, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Element 1 is unselected, yet its source projection runs before Take(2) reaches the 2nd selected element.
        // mutant: VectorWalk.EvaluateRecording without ThrowRecorded(exception) at end of scope (returns 2)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(2).Count()
        );
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(2)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath | TestScenario.Boundary)]
    public void Count_SkipTakeAfterWhereThrowInSkipped_Throws()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [0, 1, 1];
        BigInteger[] m = [1, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Skip(1) still pulls skipped elements through Where's source projection, so element 0 throws.
        // mutant: WhereDeviceState.TranslateRecorded returning null when `j < 0` instead of clamping to 0 (returns 1)
        var expected = Outcome(() => Enumerable.Range(0, a.Length)
            .Select(i => a[i] / b[i])
            .Where((_, i) => m[i] > 0)
            .Skip(1)
            .Take(1)
            .Count());
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Skip(1)
            .Take(1)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Count_TakeZeroAfterWhere_DoesNotEvaluate()
    {
        BigInteger[] a = [1];
        BigInteger[] b = [0];
        BigInteger[] m = [1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var arrM = CreateArray(m);
        using var zero = CreateScalar(0);

        // Take(0) evaluates neither source nor predicate.
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i]).Where((_, i) => m[i] > 0).Take(0).Count()
        );
        Assert.Equal((0, null), expected);

        using var count = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Where(arrM.AsVectorizedEnumerable().IsGreaterThan(zero))
            .Take(0)
            .Count();
        Assert.Equal(expected, Outcome(() => EvaluateCount(count)));
    }
}
