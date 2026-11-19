#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All stop at the deciding element (see EarlyStop.Int.cs). This file: windows (Skip, Take), whose element
// indices start at the window, a chain deeper than the recursion limit, and a shared sub-expression.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_SkipPastZeroDivisor_MatchSystemLinq()
    {
        BigInteger[] a = [4, 4, 9];
        BigInteger[] b = [0, 1, 1];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var zero = CreateScalar(0);

        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Skip(1).Select(i => a[i] / b[i] > 0).Any(static x => x)
        );
        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Skip(1)
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_TakeBeforeZeroDivisor_MatchSystemLinq()
    {
        BigInteger[] a = [4, 4, 9];
        BigInteger[] b = [1, 1, 0];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var five = CreateScalar(5);

        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Take(2).Select(i => a[i] / b[i] > 5).Any(static x => x)
        );
        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .Take(2)
            .IsGreaterThan(five)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_SquareRootWindowDecidesBeforeThrow_MatchSystemLinq()
    {
        BigInteger[] s = [9, 16, -4, 25];
        using var arrS = CreateArray(s);
        using var three = CreateScalar(3);

        // Take(3).Skip(1): sqrt(16) = 4 > 3 decides at window index 0 before -4 throws at index 1.
        // mutant: VectorSqrtExpression throwing instead of recording
        var expected = Outcome(() => s.Take(3).Skip(1).Select(static x => IsqrtContract(x) > 3).Any(static x => x));
        Assert.True(expected.Value);
        using var any = arrS.AsVectorizedEnumerable()
            .SelectSqrt()
            .Take(3)
            .Skip(1)
            .IsGreaterThan(three)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_SquareRootWindowThrowsBeforeDecide_MatchSystemLinq()
    {
        BigInteger[] s = [9, 1, -4, 25];
        using var arrS = CreateArray(s);
        using var two = CreateScalar(2);

        // Skip(1).Take(2): -4 throws at window index 1 before an element decides
        var expected = Outcome(() => s.Skip(1).Take(2).Select(static x => IsqrtContract(x) > 2).Any(static x => x));
        Assert.Equal(typeof(InvalidOperationException), expected.Error);
        using var any = arrS.AsVectorizedEnumerable()
            .SelectSqrt()
            .Skip(1)
            .Take(2)
            .IsGreaterThan(two)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_LogWindowDecidesBeforeThrow_MatchSystemLinq()
    {
        long[] values = [1, 4, -4, 16];
        using var arr = CreateFloatArray(values);
        using var one = GpuBigFloatTestHelpers.CreateFloat(_context, 1);

        // Skip(1).Take(2): ln(4) > 1 decides at window index 0 before -4 throws at index 1
        var expected = Outcome(
            () => values.Skip(1).Take(2).Select(static x => FloatLogContract(x) > 1.0).Any(static x => x)
        );
        Assert.True(expected.Value);
        using var any = arr.AsVectorizedEnumerable()
            .SelectLog()
            .Skip(1)
            .Take(2)
            .IsGreaterThan(one)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Any_DeepChain_DecidesBeforeThrow()
    {
        BigInteger[] a = [1, 2, 3];
        BigInteger[] b = [1, 0, 3];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var zero = CreateScalar(0);

        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i] + 0 > 0).Any(static x => x)
        );

        // 20 additions take the evaluation past the recursion limit, onto the explicit walk
        var chain = arrA.AsVectorizedEnumerable().ZipDivide(arrB.AsVectorizedEnumerable());
        for (var i = 0; i < 20; i++)
        {
            chain = chain.SelectAdd(zero);
        }

        using var any = chain.IsGreaterThan(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(_context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Any_SmallSharedSubexpression_MatchSystemLinq()
    {
        BigInteger[] a = [0, 0];
        using var arrA = CreateArray(a);
        using var zero = CreateScalar(0);

        // A shared chain of depth 4 is evaluated, checked and disposed end to end
        IGpuBigIntVectorExpression dag = arrA.AsVectorizedEnumerable().SelectSqrt();
        for (var i = 0; i < 4; i++)
        {
            dag = dag.ZipAdd(dag);
        }

        using var any = dag.IsGreaterThan(zero).Any();
        Assert.Equal(a.Select(static x => IsqrtContract(x) * 16 > 0).Any(static x => x), any.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_DeepSharedSubexpression_WalksEachNodeOnce()
    {
        using var arrA = CreateArray([0, 0]);
        using var mismatch = CreateArray([0]);
        using var zero = CreateScalar(0);

        // A shared chain of depth 30 (2^30 paths) under a node of mismatched lengths: building the Any walks each node
        // once, and Enter rejects the lengths before anything is evaluated or allocated. The Any is not disposed: no
        // buffer was allocated, and the disposal walks the paths of a shared chain.
        // mutant: BoolVectorQuantifier.PushOperands without the visited set (the walk does not end)
        IGpuBigIntVectorExpression dag = arrA.AsVectorizedEnumerable().SelectSqrt();
        for (var i = 0; i < 30; i++)
        {
            dag = dag.ZipAdd(dag);
        }

        var any = dag.ZipAdd(mismatch.AsVectorizedEnumerable()).IsGreaterThan(zero).Any();
        Assert.Throws<ArgumentException>(() => any.Evaluate(_context));
    }
}
