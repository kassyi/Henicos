
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntStackLinqExtensionsTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigIntStackLinqExtensionsTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    private GpuBigInt Create(BigInteger val, int numWords = 8)
    {
        bool isNeg = val < 0;
        if (isNeg) val = -val;

        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: false);
        var buffer = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, buffer.Length * 4));

        var u = new GpuBigUInt(_context, buffer);
        return new GpuBigInt(u, isNeg);
    }

    private static BigInteger ToBigInt(GpuBigInt gpuVal)
    {
        var val = gpuVal.Value.ToBigInteger();
        return gpuVal.IsNegative ? -val : val;
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Sum_CalculatesCorrectSum()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        // Act
        var sumExpr = array.AsStackEnumerable().AsParallel().Sum();
        var result = sumExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_SelectAndSum_CalculatesCorrectSum()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        var constant = Create(5);
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += (val + 5);
        }

        // Act
        var sumExpr = array.AsStackEnumerable()
            .AsParallel()
            .Select(x => x + constant)
            .Sum();

        var result = sumExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Max_CalculatesCorrectMax()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedMax = long.MinValue;

        for (int i = 0; i < numElements; i++)
        {
            // mix of positive and negative numbers
            BigInteger val = (i % 2 == 0) ? (i + 1) * 10 : -(i + 1) * 5;
            array[i] = Create(val);
            if (val > expectedMax)
            {
                expectedMax = val;
            }
        }

        // Act
        var maxExpr = array.AsStackEnumerable().AsParallel().Max();
        var result = maxExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedMax, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Min_CalculatesCorrectMin()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedMin = long.MaxValue;

        for (int i = 0; i < numElements; i++)
        {
            // mix of positive and negative numbers
            BigInteger val = (i % 2 == 0) ? (i + 1) * 10 : -(i + 1) * 5;
            array[i] = Create(val);
            if (val < expectedMin)
            {
                expectedMin = val;
            }
        }

        // Act
        var minExpr = array.AsStackEnumerable().AsParallel().Min();
        var result = minExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedMin, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Aggregate_CalculatesCorrectResult()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        // Act
        // Use Aggregate to implement a sum
        var aggregateExpr = array.AsStackEnumerable()
            .AsParallel()
            .Aggregate((a, b) => a + b);
        var result = aggregateExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Count_CalculatesCorrectCount()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];

        for (int i = 0; i < numElements; i++)
        {
            array[i] = Create((i + 1) * 10);
        }

        // Act
        var count = array.AsStackEnumerable().AsParallel().Count();

        // Assert
        Assert.Equal(numElements, count);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Average_CalculatesCorrectResult()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        var expectedAverage = expectedSum / numElements;

        // Act
        var avgExpr = array.AsStackEnumerable().AsParallel().Average();
        var result = avgExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedAverage, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Where_FiltersElementsCorrectly()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);

            // Only sum values > 100 on host for verification
            if (val > 100)
            {
                expectedSum += val;
            }
        }

        // Act
        // Use Where before Sum to filter elements. 
        // Note: predicate is evaluated on host. We read back to CPU just for the test if needed, 
        // but here we can just use ToBigInt.
        var sumExpr = array.AsStackEnumerable()
            .Where(x => ToBigInt(x) > 100)
            .AsParallel()
            .Sum();
        var result = sumExpr.Evaluate(_context);

        // Assert
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void ParallelStackEnumerable_Sum_CompileToGraph_CalculatesCorrectSum()
    {
        // Arrange
        const int numElements = 16;
        var array = new GpuBigInt[numElements];
        BigInteger expectedSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 10;
            array[i] = Create(val);
            expectedSum += val;
        }

        // Act
        var result = array.AsStackEnumerable()
            .AsParallel()
            .Sum()
            .EvaluateWithCudaGraph(_context);

        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ParallelStackEnumerable_SelectDivideAndModulo_CalculatesCorrectSum()
    {
        const int numElements = 8;
        var array = new GpuBigInt[numElements];
        var divisor = Create(7);
        BigInteger expectedDivSum = 0;
        BigInteger expectedModSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i % 2 == 0 ? 1 : -1) * ((i + 1) * 23);
            array[i] = Create(val);
            expectedDivSum += val / 7;
            expectedModSum += val % 7;
        }

        var divSumExpr = array.AsStackEnumerable()
            .AsParallel()
            .SelectDivide(divisor)
            .Sum();
        using var divResult = divSumExpr.Evaluate(_context);
        Assert.Equal(expectedDivSum, ToBigInt(divResult));

        var modSumExpr = array.AsStackEnumerable()
            .AsParallel()
            .SelectModulo(divisor)
            .Sum();
        using var modResult = modSumExpr.Evaluate(_context);
        Assert.Equal(expectedModSum, ToBigInt(modResult));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StackEnumerable_SequentialSelectDivideAndModulo_CalculatesCorrectSum()
    {
        const int numElements = 8;
        var array = new GpuBigInt[numElements];
        var divisor = Create(5);
        BigInteger expectedDivSum = 0;
        BigInteger expectedModSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger val = (i + 1) * 19;
            array[i] = Create(val);
            expectedDivSum += val / 5;
            expectedModSum += val % 5;
        }

        var divSumExpr = array.AsStackEnumerable()
            .SelectDivide(divisor)
            .Sum();
        using var divResult = divSumExpr.Evaluate(_context);
        Assert.Equal(expectedDivSum, ToBigInt(divResult));

        var modSumExpr = array.AsStackEnumerable()
            .SelectModulo(divisor)
            .Sum();
        using var modResult = modSumExpr.Evaluate(_context);
        Assert.Equal(expectedModSum, ToBigInt(modResult));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StackEnumerable_ZipDivideAndModulo_CalculatesCorrectSum()
    {
        const int numElements = 8;
        var left = new GpuBigInt[numElements];
        var right = new GpuBigInt[numElements];
        BigInteger expectedDivSum = 0;
        BigInteger expectedModSum = 0;

        for (int i = 0; i < numElements; i++)
        {
            BigInteger a = (i + 2) * 31;
            BigInteger b = (i + 1) * 4 + 1;
            left[i] = Create(a);
            right[i] = Create(b);
            expectedDivSum += a / b;
            expectedModSum += a % b;
        }

        var zipDivSumExpr = left.AsStackEnumerable()
            .AsParallel()
            .ZipDivide(right.AsStackEnumerable().AsParallel())
            .Sum();
        using var divResult = zipDivSumExpr.Evaluate(_context);
        Assert.Equal(expectedDivSum, ToBigInt(divResult));

        var zipModSumExpr = left.AsStackEnumerable()
            .AsParallel()
            .ZipModulo(right.AsStackEnumerable().AsParallel())
            .Sum();
        using var modResult = zipModSumExpr.Evaluate(_context);
        Assert.Equal(expectedModSum, ToBigInt(modResult));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigUInt_SelectAndZipModulo_CalculatesCorrectSum()
    {
        const int numElements = 8;
        var left = new GpuBigUInt[numElements];
        var right = new GpuBigUInt[numElements];
        using var divisor = new GpuBigUInt(_context, new uint[] { 7, 0, 0, 0, 0, 0, 0, 0 });
        BigInteger expectedSelectModSum = 0;
        BigInteger expectedZipModSum = 0;

        for (var i = 0; i < numElements; i++)
        {
            var aVal = (uint)((i + 2) * 37);
            var bVal = (uint)((i + 1) * 5 + 2);
            left[i] = new GpuBigUInt(_context, new uint[] { aVal, 0, 0, 0, 0, 0, 0, 0 });
            right[i] = new GpuBigUInt(_context, new uint[] { bVal, 0, 0, 0, 0, 0, 0, 0 });
            expectedSelectModSum += aVal % 7u;
            expectedZipModSum += aVal % bVal;
        }

        using var parallelSelectResult = left.AsStackEnumerable()
            .AsParallel()
            .SelectModulo(divisor)
            .Sum();
        Assert.Equal(expectedSelectModSum, parallelSelectResult.ToBigInteger());

        using var sequentialSelectResult = left.AsStackEnumerable()
            .SelectModulo(divisor)
            .Sum();
        Assert.Equal(expectedSelectModSum, sequentialSelectResult.ToBigInteger());

        using var parallelZipResult = left.AsStackEnumerable()
            .AsParallel()
            .ZipModulo(right.AsStackEnumerable().AsParallel())
            .Sum();
        Assert.Equal(expectedZipModSum, parallelZipResult.ToBigInteger());

        using var sequentialZipResult = left.AsStackEnumerable()
            .ZipModulo(right.AsStackEnumerable())
            .Sum();
        Assert.Equal(expectedZipModSum, sequentialZipResult.ToBigInteger());

        for (var i = 0; i < numElements; i++)
        {
            left[i].Dispose();
            right[i].Dispose();
        }
    }

    private static GpuBigIntExpr ToExpr(GpuBigInt value) => value;

    private GpuBigInt[] CreateArray(params long[] values)
    {
        var array = new GpuBigInt[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            array[i] = Create(values[i]);
        }

        return array;
    }

    private GpuBigUInt[] CreateUIntArray(params uint[] values)
    {
        var array = new GpuBigUInt[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            array[i] = new GpuBigUInt(_context, [values[i], 0u, 0u, 0u]);
        }

        return array;
    }

    private static void ConstructZipWithoutSelector<TLeft, TRight>(
        StackEnumerable<TLeft, GpuBigInt> left,
        StackEnumerable<TRight, GpuBigInt> right)
        where TLeft : struct, IStackEnumerator<GpuBigInt>, allows ref struct
        where TRight : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        _ = new ZipStackEnumerator<TLeft, TRight, GpuBigInt, GpuBigInt, GpuBigIntExpr>(left.Enumerator, right.Enumerator, null);

    // A null delegate is rejected up front with ArgumentNullException, not with a NullReferenceException once
    // the reduction first combines two elements (two elements, so that the combiner is actually called).
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void StackLinq_NullDelegates_Throw()
    {
        var uints = CreateUIntArray(3, 9);
        var ints = CreateArray(3, 9);

        // mutant: GpuBigNumberStackLinqExtensions.cs:136 ThrowIfNull removed
        Assert.Throws<ArgumentNullException>(() =>
            uints.AsStackEnumerable().AsParallel().Aggregate((Func<GpuBigUInt, GpuBigUInt, GpuBigUInt>)null));

        // mutant: GpuBigIntStackLinqExtensions.cs:170 ThrowIfNull removed
        Assert.Throws<ArgumentNullException>(() =>
            ints.AsStackEnumerable().AsParallel().Aggregate((Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>)null));

        // mutant: GpuBigIntStackLinqExtensions.cs:187 ThrowIfNull removed
        Assert.Throws<ArgumentNullException>(() =>
            ints.AsStackEnumerable().AsParallel().Select(ToExpr).Aggregate(null));

        // mutant: GpuBigIntStackLinqExtensions.cs:646 ThrowIfNull removed
        Assert.Throws<ArgumentNullException>(() =>
            ConstructZipWithoutSelector(ints.AsStackEnumerable(), ints.AsStackEnumerable()));

        foreach (var u in uints)
        {
            u.Dispose();
        }
    }

    // Sum, Max, Min and Average of [3, -7, 5, 10] are 11, 10, -7 and 2 (11 / 4 truncated toward zero): all four
    // differ, so a reduction that runs another one is caught. Each overload is reached through its own source type.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigInt_Reductions_OnStackAndExprSources()
    {
        var array = CreateArray(3, -7, 5, 10);

        // mutant: GpuBigIntStackLinqExtensions.cs:131 / 156 (parallel GpuBigIntExpr Max / Min)
        Assert.Equal(new BigInteger(10), ToBigInt(array.AsStackEnumerable().AsParallel().Select(ToExpr).Max().Evaluate(_context)));
        Assert.Equal(new BigInteger(-7), ToBigInt(array.AsStackEnumerable().AsParallel().Select(ToExpr).Min().Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs:526 / 548 / 570 / 618 (sequential GpuBigInt Sum / Max / Min / Average)
        Assert.Equal(new BigInteger(11), ToBigInt(array.AsStackEnumerable().Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(10), ToBigInt(array.AsStackEnumerable().Max().Evaluate(_context)));
        Assert.Equal(new BigInteger(-7), ToBigInt(array.AsStackEnumerable().Min().Evaluate(_context)));
        Assert.Equal(new BigInteger(2), ToBigInt(array.AsStackEnumerable().Average().Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs:537 / 559 / 581 (sequential GpuBigIntExpr Sum / Max / Min)
        Assert.Equal(new BigInteger(11), ToBigInt(array.AsStackEnumerable().Select(ToExpr).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(10), ToBigInt(array.AsStackEnumerable().Select(ToExpr).Max().Evaluate(_context)));
        Assert.Equal(new BigInteger(-7), ToBigInt(array.AsStackEnumerable().Select(ToExpr).Min().Evaluate(_context)));
    }

    // The balanced-tree reductions handle the sizes at their branch points: an empty sequence throws, one element
    // is returned as it is, and more than 16 elements (the initial buffer) grow the buffer, more than once for 40.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Reductions_SequenceSizeBoundaries()
    {
        // mutant: StackLinqReductionCore.cs:42 `throw` removed (Max of an empty sequence throws, as in System.Linq)
        var emptyUInts = Array.Empty<GpuBigUInt>();
        Assert.Throws<InvalidOperationException>(() => emptyUInts.AsStackEnumerable().Max());

        // mutant: StackLinqReductionCore.cs:46 one-element block removed
        var single = CreateUIntArray(7);
        using (var sum = single.AsStackEnumerable().Sum())
        {
            Assert.Equal(new BigInteger(7), sum.ToBigInteger());
        }

        // 1 + 2 + ... + 40 = 820.
        // mutant: StackLinqReductionCore.cs:63 `count == capacity` -> `!=`, L65 `capacity * 2` -> `capacity / 2`,
        // L67 Array.Copy removed
        var forty = CreateUIntArray(Enumerable.Range(1, 40).Select(static i => (uint)i).ToArray());
        using (var sum = forty.AsStackEnumerable().Sum())
        {
            Assert.Equal(new BigInteger(820), sum.ToBigInteger());
        }

        // mutant: GpuBigIntStackLinqExtensions.cs:205 `throw` removed
        var emptyInts = Array.Empty<GpuBigInt>();
        Assert.Throws<InvalidOperationException>(() => emptyInts.AsStackEnumerable().AsParallel().Average());

        // 820 / 40 = 20.5, truncated to 20.
        // mutant: GpuBigIntStackLinqExtensions.cs:218 `count == capacity` -> `!=`, L220 `capacity * 2` -> `/ 2`,
        // L222 Array.Copy removed
        var fortyInts = CreateArray(Enumerable.Range(1, 40).Select(static i => (long)i).ToArray());
        Assert.Equal(new BigInteger(20), ToBigInt(fortyInts.AsStackEnumerable().AsParallel().Average().Evaluate(_context)));

        foreach (var u in single.Concat(forty))
        {
            u.Dispose();
        }
    }

    // a = [17, 23], scalar 5, b = [5, 4]: quotient sums 7 (scalar) and 8 (zip), remainder sums 5 and 5.
    // An operator swapped to `*` gives 200 or 177 instead.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigInt_DivideModulo_AllOverloads()
    {
        var a = CreateArray(17, 23);
        var b = CreateArray(5, 4);
        var scalar = Create(5);

        // mutant: GpuBigIntStackLinqExtensions.cs:281 / 309 (parallel GpuBigInt), 295 / 323 (parallel GpuBigIntExpr)
        Assert.Equal(new BigInteger(7), ToBigInt(a.AsStackEnumerable().AsParallel().SelectDivide(scalar).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(5), ToBigInt(a.AsStackEnumerable().AsParallel().SelectModulo(scalar).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(7), ToBigInt(a.AsStackEnumerable().AsParallel().Select(ToExpr).SelectDivide(scalar).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(5), ToBigInt(a.AsStackEnumerable().AsParallel().Select(ToExpr).SelectModulo(scalar).Sum().Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs:340 / 374 (parallel GpuBigInt), 357 / 391 (parallel GpuBigIntExpr)
        Assert.Equal(new BigInteger(8), ToBigInt(a.AsStackEnumerable().AsParallel().ZipDivide(b.AsStackEnumerable().AsParallel()).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(5), ToBigInt(a.AsStackEnumerable().AsParallel().ZipModulo(b.AsStackEnumerable().AsParallel()).Sum().Evaluate(_context)));
        Assert.Equal(
            new BigInteger(8),
            ToBigInt(a.AsStackEnumerable().AsParallel().Select(ToExpr).ZipDivide(b.AsStackEnumerable().AsParallel().Select(ToExpr)).Sum().Evaluate(_context)));
        Assert.Equal(
            new BigInteger(5),
            ToBigInt(a.AsStackEnumerable().AsParallel().Select(ToExpr).ZipModulo(b.AsStackEnumerable().AsParallel().Select(ToExpr)).Sum().Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs:405 / 433 (sequential GpuBigInt), 419 / 447 (sequential GpuBigIntExpr)
        Assert.Equal(new BigInteger(7), ToBigInt(a.AsStackEnumerable().SelectDivide(scalar).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(5), ToBigInt(a.AsStackEnumerable().SelectModulo(scalar).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(7), ToBigInt(a.AsStackEnumerable().Select(ToExpr).SelectDivide(scalar).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(5), ToBigInt(a.AsStackEnumerable().Select(ToExpr).SelectModulo(scalar).Sum().Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs:464 / 498 (sequential GpuBigInt), 481 / 515 (sequential GpuBigIntExpr)
        Assert.Equal(new BigInteger(8), ToBigInt(a.AsStackEnumerable().ZipDivide(b.AsStackEnumerable()).Sum().Evaluate(_context)));
        Assert.Equal(new BigInteger(5), ToBigInt(a.AsStackEnumerable().ZipModulo(b.AsStackEnumerable()).Sum().Evaluate(_context)));
        Assert.Equal(
            new BigInteger(8),
            ToBigInt(a.AsStackEnumerable().Select(ToExpr).ZipDivide(b.AsStackEnumerable().Select(ToExpr)).Sum().Evaluate(_context)));
        Assert.Equal(
            new BigInteger(5),
            ToBigInt(a.AsStackEnumerable().Select(ToExpr).ZipModulo(b.AsStackEnumerable().Select(ToExpr)).Sum().Evaluate(_context)));
    }

    // A combiner that returns one of its inputs keeps that input alive: the reduction frees only the operand
    // that is not the result. In the balanced tree of [3, 9, 5], (9, 5) returns its left operand and (3, 9) its right one.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Aggregate_ReturningAnInput_KeepsResultAlive()
    {
        var values = CreateUIntArray(3, 9, 5);

        // mutant: GpuBigNumberStackLinqExtensions.cs:143 / 148 `!=` -> `==` (the result buffer is freed and cleared)
        using var max = values.AsStackEnumerable()
            .AsParallel()
            .Aggregate(static (x, y) => x.CompareTo(y) >= 0 ? x : y);
        Assert.Equal(new BigInteger(9), max.ToBigInteger());

        foreach (var u in values)
        {
            u.Dispose();
        }
    }
}
