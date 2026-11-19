#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All stop at the deciding element (see EarlyStop.Int.cs). This file: which chains defer their element
// exceptions (not through Where, not through an expression implemented outside GpuMath), and the recording scope.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    private static readonly BigInteger[] s_whereX = [0, 0, 1, 7];
    private static readonly BigInteger[] s_whereY = [1, 1, 0, 1];
    private static readonly BigInteger[] s_whereMask = [0, 0, 1, 1];

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_WhereChain_ThrowsAsSystemLinq()
    {
        using var zero = CreateScalar(0);
        var expected = Outcome(WhereChainReference);
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        // System.Linq divides element 2 before the filter; after the compaction the deciding element has index 1 < 2,
        // so recording by index would return true.
        // mutant: BoolVectorQuantifier.CanDefer without the Where case (returns true)
        using var x = CreateArray(s_whereX);
        using var y = CreateArray(s_whereY);
        using var m = CreateArray(s_whereMask);
        using var any = WhereChain(x, y, m, zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_ExternalFlagsOverWhere_ThrowsAsSystemLinq()
    {
        using var zero = CreateScalar(0);
        var expected = Outcome(WhereChainReference);
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        // A node implemented outside GpuMath evaluates the Where chain inside its own Evaluate, out of sight of the
        // walk.
        // mutant: BoolVectorQuantifier.CanDefer accepting a node that is neither an operator nor an array (true)
        using var x = CreateArray(s_whereX);
        using var y = CreateArray(s_whereY);
        using var m = CreateArray(s_whereMask);
        using var any = new PassThroughBoolVectorExpression(WhereChain(x, y, m, zero)).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_RecordedThenLengthMismatch_ThrowsRecorded()
    {
        using var arrA = CreateArray([4, -4]);
        using var arrC = CreateArray([1, 1]);
        using var arrD = CreateArray([1, 1, 1]);
        using var zero = CreateScalar(0);

        // The square root of element 1 is recorded before the inner Zip rejects inputs of 2 and 3 elements; the
        // evaluation that throws first in sequence (the square root, as before Any stopped early) wins.
        // mutant: BoolVectorQuantifier.Evaluate without the catch of a later exception (ArgumentException)
        using var any = arrA.AsVectorizedEnumerable()
            .SelectSqrt()
            .ZipAdd(arrC.AsVectorizedEnumerable().ZipAdd(arrD.AsVectorizedEnumerable()))
            .IsGreaterThan(zero)
            .Any();
        Assert.IsType<InvalidOperationException>(Record.Exception(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_AfterThrow_NextEvaluationThrowsDirectly()
    {
        using var arrNeg = CreateArray([-4]);
        using var zero = CreateScalar(0);

        Assert.Throws<DivideByZeroException>(() => Zipped([0, 0], [1, 0], zero, all: false));

        // The scope of the Any that threw is gone, so the next evaluation throws instead of recording.
        // mutant: PendingThrows.Scope.Dispose doing nothing (the square root is recorded, no exception)
        using var sqrt = arrNeg.AsVectorizedEnumerable().SelectSqrt();
        Assert.Throws<InvalidOperationException>(() => sqrt.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_NonDeferredChainInsideScope_ThrowsDirectly()
    {
        using var zero = CreateScalar(0);
        var expected = Outcome(WhereChainReference);
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        // A chain that Any does not defer throws by itself, even under an enclosing scope.
        // mutant: BoolVectorQuantifier.Evaluate without PendingThrows.Suspend on the chain it does not defer
        using var x = CreateArray(s_whereX);
        using var y = CreateArray(s_whereY);
        using var m = CreateArray(s_whereMask);
        using var any = WhereChain(x, y, m, zero).Any();
        using var scope = PendingThrows.Begin();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
        Assert.Null(scope.Instance.Exception);
    }

    private static bool WhereChainReference() =>
        Enumerable.Range(0, s_whereX.Length)
            .Select(static i => s_whereX[i] / s_whereY[i])
            .Where(static (_, i) => s_whereMask[i] > 0)
            .Any(static v => v > 0);

    private static IGpuBoolVectorExpression WhereChain(GpuBigIntArray x, GpuBigIntArray y, GpuBigIntArray mask,
        GpuBigInt zero) =>
        x.AsVectorizedEnumerable()
            .ZipDivide(y.AsVectorizedEnumerable())
            .Where(mask.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(zero);

    // A flag vector implemented outside GpuMath: it evaluates the expression it wraps
    private sealed class PassThroughBoolVectorExpression(IGpuBoolVectorExpression inner) : IGpuBoolVectorExpression
    {
        public int Length => inner.Length;

        public int Capacity => inner.Capacity;

        public bool TryGetLength(out int length) => inner.TryGetLength(out length);

        public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace) => inner.Evaluate(workspace);

        public void Dispose() => inner.Dispose();
    }
}
