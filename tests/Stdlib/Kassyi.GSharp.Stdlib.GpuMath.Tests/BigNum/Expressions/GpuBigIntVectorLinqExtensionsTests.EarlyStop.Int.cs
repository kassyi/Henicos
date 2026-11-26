#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All stop at the deciding element, as System.Linq: an element that throws after it does not throw. Every
// expected value or exception type comes from the same inputs through System.Linq. This file: integer chains.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    /// <summary>Any (<paramref name="All"/> false) or All of <c>A[i] / B[i] > 0</c>.</summary>
    public sealed record DivisionCase(string Name, BigInteger[] A, BigInteger[] B, bool All);

    private const int ManyBlocks = 256 * 64;

    private static readonly DivisionCase[] s_divisionCases =
    [
        // D = 0 before T = 1.
        // mutant: BoolVectorQuantifier.Evaluate throwing whenever pending.Exception is not null
        // mutant: ZeroDivisorCheck.ThrowIfFound throwing instead of recording
        new("AnyDecidesBeforeThrow", [1, 2, 3, 4], [1, 0, 3, 4], All: false),
        // mutant: GpuBigIntVectorKernels.DecidingMinIndex `(ctx.Flags[i] != 0u) == decidesOnSet` -> `!=` (D = 2 > T)
        new("AllDecidesBeforeThrow", [0, 2, 3], [1, 0, 3], All: true),
        // D = 1 before T = 2.
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `>` (throws)
        new("AllDecidesAtSecondBeforeThrow", [1, 0, 3], [1, 1, 0], All: true),
        // Flags [0, 1, 1, ?, 1] with T = 3: the lowest D = 1 < T, the highest D = 4 > T.
        // mutant: GpuBigIntVectorKernels.DecidingMinIndex `Gpu.AtomicMin` -> `Gpu.AtomicMax` (throws)
        new("AnyLowestOfSeveralDeciding", [0, 2, 3, 4, 5], [1, 1, 1, 0, 1], All: false),
        // No element decides before T.
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `>` (returns false)
        new("AnyThrowsBeforeDecide", [0, 0, 0, 0], [1, 1, 1, 0], All: false),
        // The quotient at the zero divisor is 0, a false flag: D = T = 2.
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `<` (returns false)
        new("AllThrowsAtDecidingElement", [1, 2, 3], [1, 1, 0], All: true),
        // No element decides: decidedIndex stays uint.MaxValue.
        // mutant: BoolVectorQuantifier.Evaluate `decidedIndex == uint.MaxValue ? all : !all` -> `!all`
        // mutant: BoolVectorQuantifier.Reduce initial uint.MaxValue -> 0u (Any gives true)
        new("AnyNoThrowNoDecide", [0, 0, 0, 0], [1, 1, 1, 1], All: false),
        new("AllNoThrowNoDecide", [2, 4, 6], [1, 2, 3], All: true),
        new("AnySingleElementThrows", [1], [0], All: false),
        new("AllSingleElementThrows", [1], [0], All: true),
        new("AnySingleElementTrue", [4], [2], All: false),
        new("AllSingleElementTrue", [4], [2], All: true),
        // 64 blocks: element 0 decides, element 1 divides by zero, and every element from 2 on decides as well, so a
        // plain write of any of them instead of the atomic minimum gives D > T and throws.
        // mutant: GpuBigIntVectorKernels.DecidingMinIndex `Gpu.AtomicMin(...)` -> `ctx.Result[0] = (uint)i`
        // (probabilistic: a race; red unless element 0 writes last)
        // mutant: GpuBigIntVectorKernels.DecidingMinIndex `Gpu.AtomicMin` -> `Gpu.AtomicMax`
        new("AnyManyBlocksOneDecidingBeforeThrow", ManyBlockValues(static i => i == 1 ? 0 : 1),
            ManyBlockValues(static i => i == 1 ? 0 : 1), All: false),
        new("AllManyBlocksOneDecidingBeforeThrow", ManyBlockValues(static _ => 0),
            ManyBlockValues(static i => i == 1 ? 0 : 1), All: true),
        // 64 blocks: T = 10 (block 0) before D = 800 (block 3)
        new("AnyManyBlocksThrowBeforeDecide", ManyBlockValues(static i => i >= 800 ? 1 : 0),
            ManyBlockValues(static i => i == 10 ? 0 : 1), All: false),
        // 64 blocks: D = 10 before T, the last element of the last block
        new("AnyManyBlocksThrowAtLastElement", ManyBlockValues(static i => i >= 10 ? 1 : 0),
            ManyBlockValues(static i => i == ManyBlocks - 1 ? 0 : 1), All: false),
    ];

    public static TheoryData<string> DivisionCaseNames => new(s_divisionCases.Select(static c => c.Name));

    [Theory]
    [MemberData(nameof(DivisionCaseNames))]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.SadPath | TestScenario.Boundary)]
    public void AnyAndAll_ZipDivideCase_MatchSystemLinq(string name)
    {
        var c = s_divisionCases.Single(c => c.Name == name);
        using var zero = CreateScalar(0);

        var expected = Outcome(() => Quantify(c.A, c.B, c.All));
        var actual = Outcome(() => Zipped(c.A, c.B, zero, c.All));

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_ZeroDivisorAtFirstDecidingElement_Throws()
    {
        BigInteger[] a = [1, 2];
        BigInteger[] b = [0, 1];
        using var zero = CreateScalar(0);

        // The kernel writes 0 for the zero divisor, so IsEqualTo(0) decides at D = T = 0
        var expected = Outcome(() => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i] == 0).Any(static x => x));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);
        Assert.Equal(expected, Outcome(() => ZippedIsZero(a, b, zero)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_ZeroDivisorAtLaterDecidingElement_Throws()
    {
        BigInteger[] a = [1, 2];
        BigInteger[] b = [1, 0];
        using var zero = CreateScalar(0);

        // 1 / 1 is not 0; the zero divisor of element 1 gives the quotient 0, so D = T = 1.
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `<` (returns true)
        var expected = Outcome(() => Enumerable.Range(0, a.Length).Select(i => a[i] / b[i] == 0).Any(static x => x));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);
        Assert.Equal(expected, Outcome(() => ZippedIsZero(a, b, zero)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_SquareRootThrowsAtFirstElement_Throws()
    {
        BigInteger[] s = [-1, 2];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        var expected = Outcome(() => s.Select(static x => IsqrtContract(x) == 0).Any(static x => x));
        Assert.Equal(typeof(InvalidOperationException), expected.Error);

        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().IsEqualTo(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_SquareRootTieAfterFirstElement_Throws()
    {
        BigInteger[] s = [4, -1];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        // sqrt(4) is not 0; element 1 throws, and its output, set to zero after the record, decides IsEqualTo(0):
        // D = T = 1. C# runs the projection of an element before Any reads it, so it throws.
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `<` (returns true)
        var expected = Outcome(() => s.Select(static x => IsqrtContract(x) == 0).Any(static x => x));
        Assert.Equal(typeof(InvalidOperationException), expected.Error);

        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().IsEqualTo(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_TwoThrowersAtDifferentIndices_ThrowsLowerIndex()
    {
        BigInteger[] a = [0, 0, -4, 4];
        BigInteger[] b = [1, 1, 1, 0];
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var zero = CreateScalar(0);

        // The outer square root throws at element 2, the inner division at element 3 (recorded first).
        // mutant: PendingThrows.Record without `index < _bestIndex` (DivideByZeroException)
        // mutant: VectorSqrtExpression throwing instead of recording
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => IsqrtContract(a[i] / b[i]) > 0).Any(static x => x)
        );
        Assert.Equal(typeof(InvalidOperationException), expected.Error);

        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .SelectSqrt()
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_TwoThrowersAtOneIndex_ThrowsInnerFirst()
    {
        BigInteger[] a = [0, 0, 0, -4];
        BigInteger[] c = [1, 1, 1, 0];
        using var arrA = CreateArray(a);
        using var arrC = CreateArray(c);
        using var zero = CreateScalar(0);

        // The inner square root and the outer division both throw at element 3; the inner one runs first in C#.
        // mutant: PendingThrows.Record `order < _bestOrder` -> `order > _bestOrder` (DivideByZeroException)
        var expected = Outcome(
            () => Enumerable.Range(0, a.Length).Select(i => IsqrtContract(a[i]) / c[i] > 0).Any(static x => x)
        );
        Assert.Equal(typeof(InvalidOperationException), expected.Error);

        using var any = arrA.AsVectorizedEnumerable()
            .SelectSqrt()
            .ZipDivide(arrC.AsVectorizedEnumerable())
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_ScalarZeroDivisorAfterLaterThrow_ThrowsDivision()
    {
        BigInteger[] s = [4, -4];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        // Element 0 divides by zero before the square root of element 1 throws.
        // mutant: ZeroDivisorCheck.ThrowIfAnyElement throwing instead of recording at index 0 (the catch of the
        // recorded square root wins: InvalidOperationException)
        var expected = Outcome(() => s.Select(static x => IsqrtContract(x) / 0 > 0).Any(static x => x));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().SelectDivide(zero).IsGreaterThan(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_UInt32ZeroDivisorAfterLaterThrow_ThrowsDivision()
    {
        BigInteger[] s = [4, -4];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        // mutant: VectorDivByUInt32Expression throwing instead of recording (InvalidOperationException)
        var expected = Outcome(() => s.Select(static x => IsqrtContract(x) / 0 > 0).Any(static x => x));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().SelectDivideByUInt32(0u).IsGreaterThan(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_ScalarZeroDivisorAfterThrowAtZero_ThrowsInner()
    {
        BigInteger[] s = [-4, 4];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        // The square root and the division both throw at element 0; the square root runs first.
        // mutant: PendingThrows.Record `order < _bestOrder` -> `order > _bestOrder` (DivideByZeroException)
        var expected = Outcome(() => s.Select(static x => IsqrtContract(x) / 0 > 0).Any(static x => x));
        Assert.Equal(typeof(InvalidOperationException), expected.Error);

        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().SelectDivide(zero).IsGreaterThan(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_UInt32ZeroDivisorAfterThrowAtZero_ThrowsInner()
    {
        BigInteger[] s = [-4, 4];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        var expected = Outcome(() => s.Select(static x => IsqrtContract(x) / 0 > 0).Any(static x => x));
        Assert.Equal(typeof(InvalidOperationException), expected.Error);

        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().SelectDivideByUInt32(0u).IsGreaterThan(zero).Any();
        Assert.Equal(expected, Outcome(() => any.Evaluate(Context)));
    }

    public static TheoryData<string, bool> ScalarZeroDivisorCases => new()
    {
        { "SelectDivide", false },
        { "SelectDivide", true },
        { "SelectDivideByUInt32", false },
        { "SelectDivideByUInt32", true },
        { "FloatSelectDivideByUInt32", false },
        { "FloatSelectDivideByUInt32", true },
    };

    [Theory]
    [MemberData(nameof(ScalarZeroDivisorCases))]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AnyAndAll_ScalarZeroDivisor_ThrowsDivideByZero(string operation, bool all)
    {
        BigInteger[] a = [1, 2, 3];
        var expected = Outcome(() => Quantify(a, [0, 0, 0], all));
        Assert.Equal(typeof(DivideByZeroException), expected.Error);

        using var arrA = CreateArray(a);
        using var zero = CreateScalar(0);
        using var floatArr = CreateFloatArray([1, 2, 3]);
        using var floatZero = GpuBigFloatTestHelpers.CreateFloat(Context, 0);
        var flags = operation switch
        {
            "SelectDivide" => arrA.AsVectorizedEnumerable().SelectDivide(zero).IsGreaterThan(zero),
            "SelectDivideByUInt32" => arrA.AsVectorizedEnumerable().SelectDivideByUInt32(0u).IsGreaterThan(zero),
            _ => floatArr.AsVectorizedEnumerable().SelectDivideByUInt32(0u).IsGreaterThan(floatZero),
        };

        using var quantifier = all ? flags.All() : flags.Any();
        Assert.Equal(expected, Outcome(() => quantifier.Evaluate(Context)));
    }

    // The result of an evaluation as a value or the type of the exception it throws
    private static (bool? Value, Type? Error) Outcome(Func<bool> evaluate)
    {
        try
        {
            return (evaluate(), null);
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or ArgumentException)
        {
            return (null, ex.GetType());
        }
    }

    private static (T? Value, Type? Error) Outcome<T>(Func<T> evaluate)
    {
        try
        {
            return (evaluate(), null);
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or ArgumentException)
        {
            return (default, ex.GetType());
        }
    }

    // Any (all: false) or All of a / b > 0 through System.Linq
    private static bool Quantify(BigInteger[] a, BigInteger[] b, bool all)
    {
        var flags = Enumerable.Range(0, a.Length).Select(i => a[i] / b[i] > 0);
        return all ? flags.All(static x => x) : flags.Any(static x => x);
    }

    // Any (all: false) or All of a / b > 0 through the vector expression
    private bool Zipped(BigInteger[] a, BigInteger[] b, GpuBigInt zero, bool all)
    {
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        var flags = arrA.AsVectorizedEnumerable().ZipDivide(arrB.AsVectorizedEnumerable()).IsGreaterThan(zero);
        using var quantifier = all ? flags.All() : flags.Any();
        return quantifier.Evaluate(Context);
    }

    // Any of a / b == 0 through the vector expression
    private bool ZippedIsZero(BigInteger[] a, BigInteger[] b, GpuBigInt zero)
    {
        using var arrA = CreateArray(a);
        using var arrB = CreateArray(b);
        using var any = arrA.AsVectorizedEnumerable().ZipDivide(arrB.AsVectorizedEnumerable()).IsEqualTo(zero).Any();
        return any.Evaluate(Context);
    }

    private static BigInteger[] ManyBlockValues(Func<int, int> value) =>
        Enumerable.Range(0, ManyBlocks).Select(i => (BigInteger)value(i)).ToArray();

    // floor(sqrt(x)) by Newton's method; a negative value throws as GpuMath's SelectSqrt
    private static BigInteger IsqrtContract(BigInteger x)
    {
        if (x.Sign < 0)
        {
            throw new InvalidOperationException("Cannot compute square root of a negative integer.");
        }

        if (x < 2)
        {
            return x;
        }

        var r = x;
        var next = (r + x / r) / 2;
        while (next < r)
        {
            r = next;
            next = (r + x / r) / 2;
        }

        return r;
    }
}
