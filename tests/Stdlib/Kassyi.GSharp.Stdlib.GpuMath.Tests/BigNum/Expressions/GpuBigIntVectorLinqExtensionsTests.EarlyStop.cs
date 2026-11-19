#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All stop at the deciding element, as System.Linq: an element that throws after it does not throw. Every
// expected value or exception type comes from the same inputs through System.Linq.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void AnyAndAll_DecideBeforeThrow_ReturnsWithoutThrowing()
    {
        using var zero = CreateScalar(0);

        // Any: element 0 is true (D = 0) before element 1 divides by zero (T = 1)
        // mutant: BoolVectorQuantifier.Evaluate throwing whenever pending.Exception is not null
        // mutant: ZeroDivisorCheck.ThrowIfFound throwing instead of recording
        BigInteger[] a1 = [1, 2, 3, 4];
        BigInteger[] b1 = [1, 0, 3, 4];
        Assert.Equal(Quantify(a1, b1, all: false), Zipped(a1, b1, zero, all: false));

        // All: element 0 is false (D = 0) before element 1 divides by zero (T = 1)
        // mutant: GpuBigIntVectorKernels.AllTrueMinIndex `== 0u` -> `!= 0u` (D = 2 > T = 1 throws)
        BigInteger[] a2 = [0, 2, 3];
        BigInteger[] b2 = [1, 0, 3];
        Assert.Equal(Quantify(a2, b2, all: true), Zipped(a2, b2, zero, all: true));

        // All: element 1 is false (D = 1) before element 2 divides by zero (T = 2)
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `>` (throws)
        BigInteger[] a3 = [1, 0, 3];
        BigInteger[] b3 = [1, 1, 0];
        Assert.Equal(Quantify(a3, b3, all: true), Zipped(a3, b3, zero, all: true));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Any_SeveralDecidingElements_PicksLowest()
    {
        BigInteger[] a = [0, 2, 3, 4, 5];
        BigInteger[] b = [1, 1, 1, 0, 1];
        using var zero = CreateScalar(0);

        // Flags [0, 1, 1, ?, 1] with T = 3: the lowest D = 1 < T returns true, the highest D = 4 > T would throw.
        // mutant: GpuBigIntVectorKernels.AnyTrueMinIndex `Gpu.AtomicMin` -> `Gpu.AtomicMax` (D = 4 >= T = 3 throws)
        Assert.Equal(Quantify(a, b, all: false), Zipped(a, b, zero, all: false));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AnyAndAll_ThrowBeforeDecide_ThrowsRecorded()
    {
        using var zero = CreateScalar(0);

        // Any: no element is true and element 3 divides by zero
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `>` (returns false)
        BigInteger[] a1 = [0, 0, 0, 0];
        BigInteger[] b1 = [1, 1, 1, 0];
        var expected1 = Record.Exception(() => Quantify(a1, b1, all: false));
        Assert.IsType<DivideByZeroException>(expected1);
        Assert.Equal(expected1.GetType(), Record.Exception(() => Zipped(a1, b1, zero, all: false))?.GetType());

        // All: elements 0 and 1 are true and element 2 divides by zero
        BigInteger[] a2 = [1, 2, 3];
        BigInteger[] b2 = [1, 1, 0];
        var expected2 = Record.Exception(() => Quantify(a2, b2, all: true));
        Assert.IsType<DivideByZeroException>(expected2);
        Assert.Equal(expected2.GetType(), Record.Exception(() => Zipped(a2, b2, zero, all: true))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void AnyAndAll_NoThrow_MatchSystemLinq()
    {
        using var zero = CreateScalar(0);

        // No element decides: Any is false, All is true (decidedIndex stays uint.MaxValue)
        // mutant: BoolVectorQuantifier.Evaluate `decidedIndex == uint.MaxValue ? all : !all` -> `!all`
        // mutant: BoolVectorQuantifier.Reduce initial uint.MaxValue -> 0u (Any gives true)
        BigInteger[] a1 = [0, 0, 0, 0];
        BigInteger[] b1 = [1, 1, 1, 1];
        Assert.Equal(Quantify(a1, b1, all: false), Zipped(a1, b1, zero, all: false));

        BigInteger[] a2 = [2, 4, 6];
        BigInteger[] b2 = [1, 2, 3];
        Assert.Equal(Quantify(a2, b2, all: true), Zipped(a2, b2, zero, all: true));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void AnyAndAll_ThrowAtDecidingElement_Throws()
    {
        using var zero = CreateScalar(0);

        // (1) Square root of [4, -1]: element 1 throws, and its value (zero after the recorded element) also decides
        // IsEqualTo(0) at D = T = 1; C# runs the projection of an element before Any reads it, so it throws.
        // mutant: BoolVectorQuantifier.Evaluate `(uint)pending.MinIndex <= decidedIndex` -> `<` (returns true)
        BigInteger[] s1 = [4, -1];
        using var arrS1 = CreateArray(s1);
        var expected1 = Record.Exception(() => s1.Select(static x => IsqrtContract(x) == 0).Any(static x => x));
        Assert.IsType<InvalidOperationException>(expected1);
        using var any1 = arrS1.AsVectorizedEnumerable().SelectSqrt().IsEqualTo(zero).Any();
        Assert.Equal(expected1.GetType(), Record.Exception(() => any1.Evaluate(_context))?.GetType());

        // (2) Square root of [-1, 2]: D = T = 0
        BigInteger[] s2 = [-1, 2];
        using var arrS2 = CreateArray(s2);
        var expected2 = Record.Exception(() => s2.Select(static x => IsqrtContract(x) == 0).Any(static x => x));
        Assert.IsType<InvalidOperationException>(expected2);
        using var any2 = arrS2.AsVectorizedEnumerable().SelectSqrt().IsEqualTo(zero).Any();
        Assert.Equal(expected2.GetType(), Record.Exception(() => any2.Evaluate(_context))?.GetType());

        // (3) [1, 2] / [0, 1]: the kernel writes 0 for the zero divisor, so IsEqualTo(0) decides at D = T = 0
        BigInteger[] a3 = [1, 2];
        BigInteger[] b3 = [0, 1];
        using var arrA3 = CreateArray(a3);
        using var arrB3 = CreateArray(b3);
        var expected3 = Record.Exception(
            () => Enumerable.Range(0, a3.Length).Select(i => a3[i] / b3[i] == 0).Any(static x => x)
        );
        Assert.IsType<DivideByZeroException>(expected3);
        using var any3 = arrA3.AsVectorizedEnumerable()
            .ZipDivide(arrB3.AsVectorizedEnumerable())
            .IsEqualTo(zero)
            .Any();
        Assert.Equal(expected3.GetType(), Record.Exception(() => any3.Evaluate(_context))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void AnyAndAll_SingleElement_MatchSystemLinq()
    {
        BigInteger[] one = [1];
        BigInteger[] zeroArr = [0];
        BigInteger[] four = [4];
        BigInteger[] two = [2];
        using var zero = CreateScalar(0);
        using var ten = CreateScalar(10);

        var expectedThrow = Record.Exception(() => Quantify(one, zeroArr, all: false));
        Assert.IsType<DivideByZeroException>(expectedThrow);
        var anyError = Record.Exception(() => Zipped(one, zeroArr, zero, all: false));
        var allError = Record.Exception(() => Zipped(one, zeroArr, zero, all: true));
        Assert.Equal(expectedThrow.GetType(), anyError?.GetType());
        Assert.Equal(expectedThrow.GetType(), allError?.GetType());

        Assert.Equal(Quantify(four, two, all: false), Zipped(four, two, zero, all: false));
        Assert.Equal(Quantify(four, two, all: true), Zipped(four, two, zero, all: true));

        using var arrFour = CreateArray(four);
        using var arrTwo = CreateArray(two);
        using var anyGt10 = arrFour.AsVectorizedEnumerable()
            .ZipDivide(arrTwo.AsVectorizedEnumerable())
            .IsGreaterThan(ten)
            .Any();
        using var allGt10 = arrFour.AsVectorizedEnumerable()
            .ZipDivide(arrTwo.AsVectorizedEnumerable())
            .IsGreaterThan(ten)
            .All();
        Assert.Equal(four[0] / two[0] > 10, anyGt10.Evaluate(_context));
        Assert.Equal(four[0] / two[0] > 10, allGt10.Evaluate(_context));
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

        var expected = Record.Exception(
            () => Enumerable.Range(0, a.Length).Select(i => IsqrtContract(a[i] / b[i]) > 0).Any(static x => x)
        );

        // The outer square root throws at element 2, the inner division at element 3 (recorded first).
        // mutant: PendingThrows.Record without `index < _bestIndex` (DivideByZeroException)
        // mutant: VectorSqrtExpression throwing instead of recording (the same type, but see SkipAndTake)
        using var any = arrA.AsVectorizedEnumerable()
            .ZipDivide(arrB.AsVectorizedEnumerable())
            .SelectSqrt()
            .IsGreaterThan(zero)
            .Any();

        Assert.IsType<InvalidOperationException>(expected);
        Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());
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

        var expected = Record.Exception(
            () => Enumerable.Range(0, a.Length).Select(i => IsqrtContract(a[i]) / c[i] > 0).Any(static x => x)
        );

        // The inner square root and the outer division both throw at element 3; the inner one runs first in C#.
        // mutant: PendingThrows.Record `order < _bestOrder` -> `order > _bestOrder` (DivideByZeroException)
        using var any = arrA.AsVectorizedEnumerable()
            .SelectSqrt()
            .ZipDivide(arrC.AsVectorizedEnumerable())
            .IsGreaterThan(zero)
            .Any();

        Assert.IsType<InvalidOperationException>(expected);
        Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_ScalarZeroDivisorAfterLaterThrow_ThrowsDivision()
    {
        BigInteger[] s = [4, -4];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        // Element 0 divides by zero before the square root of element 1 throws
        var expected = Record.Exception(() => s.Select(static x => IsqrtContract(x) / 0 > 0).Any(static x => x));
        Assert.IsType<DivideByZeroException>(expected);

        // mutant: ZeroDivisorCheck.ThrowIfAnyElement throwing instead of recording at index 0 (the catch of the
        // recorded square root wins: InvalidOperationException)
        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().SelectDivide(zero).IsGreaterThan(zero).Any();
        Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());

        // mutant: VectorDivByUInt32Expression throwing instead of recording (InvalidOperationException)
        using var anyUInt32 = arrS.AsVectorizedEnumerable()
            .SelectSqrt()
            .SelectDivideByUInt32(0u)
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected.GetType(), Record.Exception(() => anyUInt32.Evaluate(_context))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_ScalarZeroDivisorAfterThrowAtZero_ThrowsInner()
    {
        BigInteger[] s = [-4, 4];
        using var arrS = CreateArray(s);
        using var zero = CreateScalar(0);

        // The square root and the division both throw at element 0; the square root runs first
        var expected = Record.Exception(() => s.Select(static x => IsqrtContract(x) / 0 > 0).Any(static x => x));
        Assert.IsType<InvalidOperationException>(expected);

        // mutant: PendingThrows.Record `order < _bestOrder` -> `order > _bestOrder` (DivideByZeroException)
        using var any = arrS.AsVectorizedEnumerable().SelectSqrt().SelectDivide(zero).IsGreaterThan(zero).Any();
        Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());

        using var anyUInt32 = arrS.AsVectorizedEnumerable()
            .SelectSqrt()
            .SelectDivideByUInt32(0u)
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expected.GetType(), Record.Exception(() => anyUInt32.Evaluate(_context))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void AnyAndAll_ScalarDivideByZero_ThrowsDivideByZero()
    {
        BigInteger[] a = [1, 2, 3];
        using var arrA = CreateArray(a);
        using var zero = CreateScalar(0);

        var expected = Record.Exception(() => a.Select(static x => x / 0 > 0).Any(static x => x));
        Assert.IsType<DivideByZeroException>(expected);

        using var any = arrA.AsVectorizedEnumerable().SelectDivide(zero).IsGreaterThan(zero).Any();
        using var all = arrA.AsVectorizedEnumerable().SelectDivide(zero).IsGreaterThan(zero).All();
        Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());
        Assert.Equal(expected.GetType(), Record.Exception(() => all.Evaluate(_context))?.GetType());

        using var anyUInt32 = arrA.AsVectorizedEnumerable().SelectDivideByUInt32(0u).IsGreaterThan(zero).Any();
        using var allUInt32 = arrA.AsVectorizedEnumerable().SelectDivideByUInt32(0u).IsGreaterThan(zero).All();
        Assert.Equal(expected.GetType(), Record.Exception(() => anyUInt32.Evaluate(_context))?.GetType());
        Assert.Equal(expected.GetType(), Record.Exception(() => allUInt32.Evaluate(_context))?.GetType());

        long[] floatValues = [1, 2];
        using var floatArr = CreateFloatArray(floatValues);
        using var floatZero = GpuBigFloatTestHelpers.CreateFloat(_context, 0);
        using var floatAny = floatArr.AsVectorizedEnumerable().SelectDivideByUInt32(0u).IsGreaterThan(floatZero).Any();
        using var floatAll = floatArr.AsVectorizedEnumerable().SelectDivideByUInt32(0u).IsGreaterThan(floatZero).All();
        Assert.Equal(expected.GetType(), Record.Exception(() => floatAny.Evaluate(_context))?.GetType());
        Assert.Equal(expected.GetType(), Record.Exception(() => floatAll.Evaluate(_context))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void AnyAndAll_ManyBlocks_PicksLowestAcrossBlocks()
    {
        const int n = 1025; // 5 blocks of 256 threads
        using var zero = CreateScalar(0);

        // Direction 1: D = 10 (block 0) < T = 512 (block 2), with true flags up to 1024 (block 4) after T.
        // mutant: GpuBigIntVectorKernels.AnyTrueMinIndex `Gpu.AtomicMin` -> `Gpu.AtomicMax` (D = 1024 >= T)
        // mutant: GpuBigIntVectorKernels.AnyTrueMinIndex `Gpu.AtomicMin(...)` -> `ctx.Result[0] = (uint)i`
        // (probabilistic: the result depends on which write lands last)
        var a1 = new BigInteger[n];
        var b1 = new BigInteger[n];
        for (var i = 0; i < n; i++)
        {
            a1[i] = i >= 10 ? 1 : 0;
            b1[i] = i == 512 ? 0 : 1;
        }

        Assert.Equal(Quantify(a1, b1, all: false), Zipped(a1, b1, zero, all: false));

        // Direction 2: T = 10 (block 0) < D = 800 (block 3)
        var a2 = new BigInteger[n];
        var b2 = new BigInteger[n];
        for (var i = 0; i < n; i++)
        {
            a2[i] = i >= 800 ? 1 : 0;
            b2[i] = i == 10 ? 0 : 1;
        }

        var expected2 = Record.Exception(() => Quantify(a2, b2, all: false));
        Assert.IsType<DivideByZeroException>(expected2);
        Assert.Equal(expected2.GetType(), Record.Exception(() => Zipped(a2, b2, zero, all: false))?.GetType());

        // Direction 3: D = 10 < T = 1024, the last element of the last block
        var a3 = new BigInteger[n];
        var b3 = new BigInteger[n];
        for (var i = 0; i < n; i++)
        {
            a3[i] = i >= 10 ? 1 : 0;
            b3[i] = i == 1024 ? 0 : 1;
        }

        Assert.Equal(Quantify(a3, b3, all: false), Zipped(a3, b3, zero, all: false));

        // Direction 4 (All): D = 10 (block 0) < T = 512 (block 2), with false flags in blocks 2 to 4 after T.
        // mutant: GpuBigIntVectorKernels.AllTrueMinIndex `Gpu.AtomicMin(...)` -> `ctx.Result[0] = (uint)i`
        // (probabilistic)
        var a4 = new BigInteger[n];
        var b4 = new BigInteger[n];
        for (var i = 0; i < n; i++)
        {
            a4[i] = i < 10 || i == 512 ? 1 : 0;
            b4[i] = i == 512 ? 0 : 1;
        }

        Assert.Equal(Quantify(a4, b4, all: true), Zipped(a4, b4, zero, all: true));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath | TestScenario.SadPath)]
    public void AnyAndAll_FloatChains_MatchSystemLinq()
    {
        using var zero = GpuBigFloatTestHelpers.CreateFloat(_context, 0);
        using var one = GpuBigFloatTestHelpers.CreateFloat(_context, 1);

        // ZipDivide, Any: D = 0 < T = 1
        long[] da1 = [1, 2, 3, 4];
        long[] db1 = [1, 0, 3, 4];
        using var fda1 = CreateFloatArray(da1);
        using var fdb1 = CreateFloatArray(db1);
        var expDivAnyDecide = Enumerable.Range(0, da1.Length)
            .Select(i => (decimal)da1[i] / db1[i] > 0m)
            .Any(static x => x);
        using var divAnyDecide = fda1.AsVectorizedEnumerable()
            .ZipDivide(fdb1.AsVectorizedEnumerable())
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expDivAnyDecide, divAnyDecide.Evaluate(_context));

        // ZipDivide, Any: T = 1 < D; All: D = 0 < T = 1
        long[] da2 = [0, 2, 3];
        long[] db2 = [1, 0, 3];
        using var fda2 = CreateFloatArray(da2);
        using var fdb2 = CreateFloatArray(db2);
        var expDivAnyThrow = Record.Exception(
            () => Enumerable.Range(0, da2.Length).Select(i => (decimal)da2[i] / db2[i] > 0m).Any(static x => x)
        );
        Assert.IsType<DivideByZeroException>(expDivAnyThrow);
        using var divAnyThrow = fda2.AsVectorizedEnumerable()
            .ZipDivide(fdb2.AsVectorizedEnumerable())
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expDivAnyThrow.GetType(), Record.Exception(() => divAnyThrow.Evaluate(_context))?.GetType());

        var expDivAllDecide = Enumerable.Range(0, da2.Length)
            .Select(i => (decimal)da2[i] / db2[i] > 0m)
            .All(static x => x);
        using var divAllDecide = fda2.AsVectorizedEnumerable()
            .ZipDivide(fdb2.AsVectorizedEnumerable())
            .IsGreaterThan(zero)
            .All();
        Assert.Equal(expDivAllDecide, divAllDecide.Evaluate(_context));

        // SelectLog, Any: ln(4) > 1 at D = 0 before the logarithm of -4 throws at T = 1
        // mutant: FloatVectorElementwiseHelper.ExecuteUnary throwing instead of recording
        long[] la1 = [4, -4, 16];
        using var fla1 = CreateFloatArray(la1);
        var expLogAnyDecide = la1.Select(static x => FloatLogContract(x) > 1.0).Any(static x => x);
        using var logAnyDecide = fla1.AsVectorizedEnumerable().SelectLog().IsGreaterThan(one).Any();
        Assert.Equal(expLogAnyDecide, logAnyDecide.Evaluate(_context));

        // SelectLog, Any: T = 1 before any element decides; All: ln(1) = 0 decides at D = 0
        long[] la2 = [1, -4, 16];
        using var fla2 = CreateFloatArray(la2);
        var expLogAnyThrow = Record.Exception(
            () => la2.Select(static x => FloatLogContract(x) > 1.0).Any(static x => x)
        );
        Assert.IsType<InvalidOperationException>(expLogAnyThrow);
        using var logAnyThrow = fla2.AsVectorizedEnumerable().SelectLog().IsGreaterThan(one).Any();
        Assert.Equal(expLogAnyThrow.GetType(), Record.Exception(() => logAnyThrow.Evaluate(_context))?.GetType());

        var expLogAllDecide = la2.Select(static x => FloatLogContract(x) > 1.0).All(static x => x);
        using var logAllDecide = fla2.AsVectorizedEnumerable().SelectLog().IsGreaterThan(one).All();
        Assert.Equal(expLogAllDecide, logAllDecide.Evaluate(_context));

        // ZipPow, Any: 4^2 > 1 at D = 0 before the negative base throws at T = 1
        // mutant: FloatVectorElementwiseHelper.ExecuteBinary throwing instead of recording
        long[] pa1 = [4, -2, 9];
        long[] pe1 = [2, 2, 2];
        using var fpa1 = CreateFloatArray(pa1);
        using var fpe1 = CreateFloatArray(pe1);
        var expPowAnyDecide = Enumerable.Range(0, pa1.Length)
            .Select(i => FloatPowContract(pa1[i], pe1[i]) > 1.0)
            .Any(static x => x);
        using var powAnyDecide = fpa1.AsVectorizedEnumerable()
            .ZipPow(fpe1.AsVectorizedEnumerable())
            .IsGreaterThan(one)
            .Any();
        Assert.Equal(expPowAnyDecide, powAnyDecide.Evaluate(_context));

        // SelectLog then a zero divisor: element 0 divides by zero before the logarithm of element 1 throws
        // mutant: FloatVectorDivByUInt32Expression throwing instead of recording (InvalidOperationException)
        long[] lz = [4, -4];
        using var flz = CreateFloatArray(lz);
        var expLogDivide = Record.Exception(
            () => lz.Select(static x => (decimal)FloatLogContract(x) / 0m > 0m).Any(static x => x)
        );
        Assert.IsType<DivideByZeroException>(expLogDivide);
        using var logDivide = flz.AsVectorizedEnumerable()
            .SelectLog()
            .SelectDivideByUInt32(0u)
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expLogDivide.GetType(), Record.Exception(() => logDivide.Evaluate(_context))?.GetType());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void AnyAndAll_SkipAndTake_MatchSystemLinq()
    {
        using var zero = CreateScalar(0);
        using var two = CreateScalar(2);
        using var three = CreateScalar(3);
        using var five = CreateScalar(5);

        // Skip passes over an earlier zero divisor
        BigInteger[] a1 = [4, 4, 9];
        BigInteger[] b1 = [0, 1, 1];
        using var arrA1 = CreateArray(a1);
        using var arrB1 = CreateArray(b1);
        var expSkip = Enumerable.Range(0, a1.Length).Skip(1).Select(i => a1[i] / b1[i] > 0).Any(static x => x);
        using var anySkip = arrA1.AsVectorizedEnumerable()
            .ZipDivide(arrB1.AsVectorizedEnumerable())
            .Skip(1)
            .IsGreaterThan(zero)
            .Any();
        Assert.Equal(expSkip, anySkip.Evaluate(_context));

        // Take leaves out a later zero divisor
        BigInteger[] a2 = [4, 4, 9];
        BigInteger[] b2 = [1, 1, 0];
        using var arrA2 = CreateArray(a2);
        using var arrB2 = CreateArray(b2);
        var expTake = Enumerable.Range(0, a2.Length).Take(2).Select(i => a2[i] / b2[i] > 5).Any(static x => x);
        using var anyTake = arrA2.AsVectorizedEnumerable()
            .ZipDivide(arrB2.AsVectorizedEnumerable())
            .Take(2)
            .IsGreaterThan(five)
            .Any();
        Assert.Equal(expTake, anyTake.Evaluate(_context));

        // Take(3).Skip(1) of square roots: sqrt(16) = 4 > 3 decides at window index 0 before -4 throws at index 1
        // mutant: VectorSqrtExpression throwing instead of recording
        BigInteger[] s1 = [9, 16, -4, 25];
        using var arrS1 = CreateArray(s1);
        var expSqrtSlice = s1.Take(3).Skip(1).Select(static x => IsqrtContract(x) > 3).Any(static x => x);
        using var anySqrtSlice = arrS1.AsVectorizedEnumerable()
            .SelectSqrt()
            .Take(3)
            .Skip(1)
            .IsGreaterThan(three)
            .Any();
        Assert.Equal(expSqrtSlice, anySqrtSlice.Evaluate(_context));

        // Skip(1).Take(2) of square roots: -4 throws before an element decides
        BigInteger[] s2 = [9, 1, -4, 25];
        using var arrS2 = CreateArray(s2);
        var expSqrtThrow = Record.Exception(
            () => s2.Skip(1).Take(2).Select(static x => IsqrtContract(x) > 2).Any(static x => x)
        );
        Assert.IsType<InvalidOperationException>(expSqrtThrow);
        using var anySqrtThrow = arrS2.AsVectorizedEnumerable()
            .SelectSqrt()
            .Skip(1)
            .Take(2)
            .IsGreaterThan(two)
            .Any();
        Assert.Equal(expSqrtThrow.GetType(), Record.Exception(() => anySqrtThrow.Evaluate(_context))?.GetType());

        // Skip(1).Take(2) of logarithms: ln(4) > 1 decides at window index 0 before -4 throws at index 1
        using var one = GpuBigFloatTestHelpers.CreateFloat(_context, 1);
        long[] fl = [1, 4, -4, 16];
        using var arrFl = CreateFloatArray(fl);
        var expFloatSlice = fl.Skip(1).Take(2).Select(static x => FloatLogContract(x) > 1.0).Any(static x => x);
        using var anyFloatSlice = arrFl.AsVectorizedEnumerable()
            .SelectLog()
            .Skip(1)
            .Take(2)
            .IsGreaterThan(one)
            .Any();
        Assert.Equal(expFloatSlice, anyFloatSlice.Evaluate(_context));
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

        var expected = Enumerable.Range(0, a.Length).Select(i => a[i] / b[i] + 0 > 0).Any(static x => x);

        // 20 additions take the evaluation past the recursion limit, onto the explicit walk
        var chain = arrA.AsVectorizedEnumerable().ZipDivide(arrB.AsVectorizedEnumerable());
        for (var i = 0; i < 20; i++)
        {
            chain = chain.SelectAdd(zero);
        }

        using var any = chain.IsGreaterThan(zero).Any();
        Assert.Equal(expected, any.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_WhereChain_ThrowsAsSystemLinq()
    {
        using var zero = CreateScalar(0);
        var expected = Record.Exception(static () => WhereChainReference());
        Assert.IsType<DivideByZeroException>(expected);

        // System.Linq divides element 2 before the filter; after the compaction the deciding element has index 1 < 2,
        // so recording by index would return true.
        // mutant: BoolVectorQuantifier.CanDefer without the Where case (returns true)
        var (x, y, m) = WhereChainOperands();
        using (x)
        using (y)
        using (m)
        {
            using var any = WhereChain(x, y, m, zero).Any();
            Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_ExternalFlagsOverWhere_ThrowsAsSystemLinq()
    {
        using var zero = CreateScalar(0);
        var expected = Record.Exception(static () => WhereChainReference());
        Assert.IsType<DivideByZeroException>(expected);

        // A node implemented outside GpuMath evaluates the Where chain inside its own Evaluate, out of sight of the
        // walk.
        // mutant: BoolVectorQuantifier.CanDefer accepting a node that is neither an operator nor an array (true)
        var (x, y, m) = WhereChainOperands();
        using (x)
        using (y)
        using (m)
        {
            using var any = new PassThroughBoolVectorExpression(WhereChain(x, y, m, zero)).Any();
            Assert.Equal(expected.GetType(), Record.Exception(() => any.Evaluate(_context))?.GetType());
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_RecordedThenLengthMismatch_ThrowsRecorded()
    {
        BigInteger[] a = [4, -4];
        using var arrA = CreateArray(a);
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
    [Scenario(TestScenario.Boundary)]
    public void AnyAndAll_SharedSubexpression_WalksEachNodeOnce()
    {
        BigInteger[] a = [0, 0];
        using var arrA = CreateArray(a);
        using var mismatch = CreateArray([0]);
        using var zero = CreateScalar(0);

        // A small shared chain (depth 4) is evaluated, checked and disposed end to end
        IGpuBigIntVectorExpression smallDag = arrA.AsVectorizedEnumerable().SelectSqrt();
        for (var i = 0; i < 4; i++)
        {
            smallDag = smallDag.ZipAdd(smallDag);
        }

        using var smallAny = smallDag.IsGreaterThan(zero).Any();
        Assert.Equal(a.Select(static x => IsqrtContract(x) * 16 > 0).Any(static x => x), smallAny.Evaluate(_context));

        // A shared chain of depth 30 under a node of mismatched lengths: CanDefer walks the 2^30 paths once per node
        // before Enter rejects the lengths, and nothing is evaluated or allocated. The Any is not disposed: no buffer
        // was allocated, and the disposal walks the paths of a shared chain.
        // mutant: BoolVectorQuantifier.CanDefer without the visited set (the walk does not end)
        IGpuBigIntVectorExpression deepDag = arrA.AsVectorizedEnumerable().SelectSqrt();
        for (var i = 0; i < 30; i++)
        {
            deepDag = deepDag.ZipAdd(deepDag);
        }

        var deepAny = deepDag.ZipAdd(mismatch.AsVectorizedEnumerable()).IsGreaterThan(zero).Any();
        Assert.Throws<ArgumentException>(() => deepAny.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Any_AfterThrow_NextEvaluationThrowsDirectly()
    {
        BigInteger[] a = [0, 0];
        BigInteger[] b = [1, 0];
        using var arrNeg = CreateArray([-4]);
        using var zero = CreateScalar(0);

        Assert.Throws<DivideByZeroException>(() => Zipped(a, b, zero, all: false));

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
        var expected = Record.Exception(static () => WhereChainReference());
        Assert.IsType<DivideByZeroException>(expected);

        // A chain that Any does not defer throws by itself, even under an enclosing scope.
        // mutant: BoolVectorQuantifier.Evaluate without PendingThrows.Suspend on the chain it does not defer
        var (x, y, m) = WhereChainOperands();
        using (x)
        using (y)
        using (m)
        {
            using var any = WhereChain(x, y, m, zero).Any();
            using var scope = PendingThrows.Begin();
            var actual = Record.Exception(() => any.Evaluate(_context));
            Assert.Equal(expected.GetType(), actual?.GetType());
            Assert.Null(scope.Instance.Exception);
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
        return quantifier.Evaluate(_context);
    }

    private static readonly BigInteger[] s_whereX = [0, 0, 1, 7];
    private static readonly BigInteger[] s_whereY = [1, 1, 0, 1];
    private static readonly BigInteger[] s_whereMask = [0, 0, 1, 1];

    private static bool WhereChainReference() =>
        Enumerable.Range(0, s_whereX.Length)
            .Select(static i => s_whereX[i] / s_whereY[i])
            .Where(static (_, i) => s_whereMask[i] > 0)
            .Any(static v => v > 0);

    private (GpuBigIntArray X, GpuBigIntArray Y, GpuBigIntArray Mask) WhereChainOperands() =>
        (CreateArray(s_whereX), CreateArray(s_whereY), CreateArray(s_whereMask));

    private static IGpuBoolVectorExpression WhereChain(GpuBigIntArray x, GpuBigIntArray y, GpuBigIntArray mask,
        GpuBigInt zero) =>
        x.AsVectorizedEnumerable()
            .ZipDivide(y.AsVectorizedEnumerable())
            .Where(mask.AsVectorizedEnumerable().IsGreaterThan(zero))
            .IsGreaterThan(zero);

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

    private GpuBigFloatArray CreateFloatArray(long[] values, int numWords = 4)
    {
        var array = new GpuBigFloatArray(_context, values.Length, numWords);
        for (var i = 0; i < values.Length; i++)
        {
            using var element =
                GpuBigFloatTestHelpers.CreateFloat(_context, values[i], exponent: 0, numWords: numWords);
            array.SetElement(i, element);
        }

        return array;
    }

    private static double FloatLogContract(long x) =>
        x <= 0 ? throw new InvalidOperationException("Log of non-positive number.") : Math.Log(x);

    private static double FloatPowContract(long x, long y) =>
        x < 0 ? throw new InvalidOperationException("Pow with negative base is not supported.") : Math.Pow(x, y);

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
