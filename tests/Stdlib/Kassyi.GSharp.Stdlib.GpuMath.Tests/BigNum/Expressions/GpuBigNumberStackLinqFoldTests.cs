namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// The order of the StackLinq reductions of big numbers without AsParallel(): Aggregate and the rounded Sum / Average
// fold from the left as Enumerable.Aggregate does; the exact integer Sum / Average keep the balanced tree.
[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigNumberStackLinqFoldTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigNumberStackLinqFoldTests()
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

    // [10, 3, 2, 1] with a - b: the left fold of Enumerable.Aggregate gives ((10 - 3) - 2) - 1 = 4, the balanced tree
    // of AsParallel() (10 - 3) - (2 - 1) = 6. [64, 8, 4, 2] with a / b: ((64 / 8) / 4) / 2 = 1 against
    // (64 / 8) / (4 / 2) = 4.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Aggregate_WithoutAsParallel_FoldsFromTheLeft()
    {
        var ints = CreateInts(8, 10, 3, 2, 1);

        // mutant: GpuBigIntStackLinqExtensions.cs sequential Aggregate (GpuBigInt)
        //   -> source.AsParallel().Aggregate(func)
        Assert.Equal(
            new BigInteger(4),
            ToBigInt(ints.AsStackEnumerable().Aggregate(static (a, b) => a - b).Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs sequential Aggregate (GpuBigIntExpr)
        //   -> source.AsParallel().Aggregate(func)
        Assert.Equal(
            new BigInteger(4),
            ToBigInt(ints.AsStackEnumerable().Select(ToExpr).Aggregate(static (a, b) => a - b).Evaluate(_context)));

        // The marker keeps the tree.
        Assert.Equal(
            new BigInteger(6),
            ToBigInt(ints.AsStackEnumerable().AsParallel().Aggregate(static (a, b) => a - b).Evaluate(_context)));

        var uints = CreateUIntArray(64, 8, 4, 2);

        // mutant: GpuBigNumberStackLinqExtensions.cs Aggregate (GpuBigUInt) -> source.AsParallel().Aggregate(func)
        using (var quotient = uints.AsStackEnumerable().Aggregate(static (a, b) => Divide(a, b)))
        {
            Assert.Equal(BigInteger.One, quotient.ToBigInteger());
        }

        using (var treeQuotient = uints.AsStackEnumerable().AsParallel().Aggregate(static (a, b) => Divide(a, b)))
        {
            Assert.Equal(new BigInteger(4), treeQuotient.ToBigInteger());
        }

        foreach (var u in uints)
        {
            u.Dispose();
        }
    }

    // A left fold makes the expression as deep as the sequence. The width of a signed node was read twice from its left
    // child at every level, so reading it took 2^depth steps; a sequence of 1000 elements never finished. The width is
    // read on another thread so that the old behavior fails in bounded time (it needs no CUDA context). Evaluate
    // recursed once per level and overflowed the stack of the test host at this length; it now walks the chain in a
    // loop. Two folds of 1000 elements take about 0.1 s.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Aggregate_WithoutAsParallel_OfALongSequence_EvaluatesInLinearTime()
    {
        const int Count = 1000;
        var values = new long[Count];
        Array.Fill(values, 1L);
        var ints = CreateInts(2, values);

        var signed = ints.AsStackEnumerable().Aggregate(static (a, b) => a + b);
        Assert.Equal(Count - 1, AdditionDepth(signed.Node));

        // mutant: SignedExpressions.cs SignedBinaryExpression.NumWords -> Left.NumWords read twice, uncached
        var width = Task.Run(() => signed.Node.NumWords);
        Assert.True(width.Wait(TimeSpan.FromSeconds(10)), "Reading the width of the folded expression did not finish.");
        // A left fold of additions grows by one bit per addition: 64 + 999 bits
        Assert.Equal(BigIntWidths.Words(64 + Count - 1), width.Result);

        // mutant: SignedExpressions.cs EvaluateLeft -> Left.Evaluate(workspace) (the test host crashes)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using (var sum = signed.Evaluate(_context))
        {
            Assert.Equal(new BigInteger(Count), ToBigInt(sum));
        }

        var expressionSum = ints.AsStackEnumerable().Select(ToExpr).Aggregate(static (a, b) => a + b);
        using (var sum = expressionSum.Evaluate(_context))
        {
            Assert.Equal(new BigInteger(Count), ToBigInt(sum));
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Evaluating two folds of {Count} elements took {stopwatch.Elapsed}.");

        foreach (var value in ints)
        {
            value.Dispose();
        }
    }

    // Integer addition is exact, so Sum and Average without AsParallel() keep the balanced tree: the value equals the
    // BigInteger sum in any order, and the tree of eight elements is three additions deep where a left fold is seven.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void SumAverage_ExactIntegers_WithoutAsParallel_KeepTheBalancedTree()
    {
        long[] values = [1_000_000_007, -3, 2_147_483_647, 5, -1_000_000_000_000, 11, 13, 17];
        var expectedSum = BigInteger.Zero;
        foreach (var value in values)
        {
            expectedSum += value;
        }

        var ints = CreateInts(8, values);

        var sum = ints.AsStackEnumerable().Sum();
        Assert.Equal(expectedSum, ToBigInt(sum.Evaluate(_context)));

        // mutant: GpuBigIntStackLinqExtensions.cs sequential Sum (GpuBigInt) -> left fold
        Assert.Equal(3, AdditionDepth(sum.Node));

        // mutant: GpuBigIntStackLinqExtensions.cs sequential Sum (GpuBigIntExpr) -> left fold
        var exprSum = ints.AsStackEnumerable().Select(ToExpr).Sum();
        Assert.Equal(expectedSum, ToBigInt(exprSum.Evaluate(_context)));
        Assert.Equal(3, AdditionDepth(exprSum.Node));

        var positives = CreateInts(8, 10, 20, 30, 41);
        Assert.Equal(new BigInteger(25), ToBigInt(positives.AsStackEnumerable().Average().Evaluate(_context)));

        uint[] unsignedValues = [uint.MaxValue, uint.MaxValue, 7, uint.MaxValue];
        var expectedUnsignedSum = (BigInteger)uint.MaxValue * 3 + 7;
        var uints = CreateUIntArray(unsignedValues);
        using (var unsignedSum = uints.AsStackEnumerable().Sum())
        {
            Assert.Equal(expectedUnsignedSum, unsignedSum.ToBigInteger());
        }

        using (var unsignedAverage = uints.AsStackEnumerable().Average())
        {
            Assert.Equal(expectedUnsignedSum / unsignedValues.Length, unsignedAverage.ToBigInteger());
        }

        foreach (var u in uints)
        {
            u.Dispose();
        }
    }

    // A sum gets one word more than its wider operand, so 2^200 + 1 does not fit the two words of the first sum and the
    // order of the additions of [2^200, 1, -2^200, 1] shows in the result. Floating-point additions round, so without
    // AsParallel() the reductions fold from the left as Enumerable.Aggregate does; the references are built here with
    // the same operators in that order, and the balanced tree is built beside them to show that the inputs tell the
    // two orders apart.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloat_SumAverageAggregate_WithoutAsParallel_FoldFromTheLeft()
    {
        using var big = CreateFloat(1, exponent: 200);
        using var one = CreateFloat(1);
        using var negativeBig = CreateFloat(-1, exponent: 200);
        using var lastOne = CreateFloat(1);
        using var four = CreateFloat(4);
        var array = new[] { big, one, negativeBig, lastOne };

        using GpuBigFloat left01 = big + one;
        using GpuBigFloat left012 = left01 + negativeBig;
        using GpuBigFloat leftSum = left012 + lastOne;
        using GpuBigFloat right23 = negativeBig + lastOne;
        using GpuBigFloat treeSum = left01 + right23;
        using GpuBigFloat leftAverage = leftSum / four;
        Assert.NotEqual(0, leftSum.CompareTo(treeSum));

        // mutant: GpuBigNumberStackLinqExtensions.cs sequential Sum -> source.AsParallel().Sum()
        using var sum = array.AsStackEnumerable().Sum();
        Assert.Equal(0, leftSum.CompareTo(sum));

        // mutant: GpuBigNumberStackLinqExtensions.cs sequential Average -> source.AsParallel().Average()
        using var average = array.AsStackEnumerable().Average();
        Assert.Equal(0, leftAverage.CompareTo(average));

        // mutant: GpuBigNumberStackLinqExtensions.cs Aggregate (GpuBigFloat) -> source.AsParallel().Aggregate(func)
        using var aggregate = array.AsStackEnumerable().Aggregate(static (a, b) => a + b);
        Assert.Equal(0, leftSum.CompareTo(aggregate));

        // The marker keeps the tree.
        using var parallelSum = array.AsStackEnumerable().AsParallel().Sum();
        Assert.Equal(0, treeSum.CompareTo(parallelSum));
    }

    // [64, 8, 4, 2] with a / b: ((64 / 8) / 4) / 2 = 1 from the left, (64 / 8) / (4 / 2) = 4 as a balanced tree.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigUFloat_Aggregate_WithoutAsParallel_FoldsFromTheLeft()
    {
        GpuBigUFloat[] array =
        [
            new(new GpuBigUInt(_context, [64u, 0u]), 0),
            new(new GpuBigUInt(_context, [8u, 0u]), 0),
            new(new GpuBigUInt(_context, [4u, 0u]), 0),
            new(new GpuBigUInt(_context, [2u, 0u]), 0),
        ];

        try
        {
            // mutant: GpuBigNumberStackLinqExtensions.cs Aggregate (GpuBigUFloat)
            //   -> source.AsParallel().Aggregate(func)
            using var quotient = array.AsStackEnumerable().Aggregate(static (a, b) => a / b);
            using var expectedOne = new GpuBigUFloat(new GpuBigUInt(_context, [1u, 0u]), 0);
            Assert.Equal(0, expectedOne.CompareTo(quotient));

            using var treeQuotient = array.AsStackEnumerable().AsParallel().Aggregate(static (a, b) => a / b);
            using var expectedFour = new GpuBigUFloat(new GpuBigUInt(_context, [4u, 0u]), 0);
            Assert.Equal(0, expectedFour.CompareTo(treeQuotient));
        }
        finally
        {
            foreach (var value in array)
            {
                value.Dispose();
            }
        }
    }

    // The number of additions on the longest path from the root to a leaf. A loop, so that a chain of a thousand
    // nodes does not depend on the stack size of the test thread.
    private static int AdditionDepth(IGpuBigIntExpression node)
    {
        var depth = 0;
        var pending = new Stack<(IGpuBigIntExpression Node, int Depth)>();
        pending.Push((node, 0));
        while (pending.Count > 0)
        {
            var (current, currentDepth) = pending.Pop();
            if (current is SignedBinaryExpression binary)
            {
                pending.Push((binary.Left, currentDepth + 1));
                pending.Push((binary.Right, currentDepth + 1));
            }
            else
            {
                depth = Math.Max(depth, currentDepth);
            }
        }

        return depth;
    }

    private static T Divide<T>(T left, T right)
        where T : struct, IGpuBigNumber<T> =>
        left / right;

    private static GpuBigIntExpr ToExpr(GpuBigInt value) => value;

    private static BigInteger ToBigInt(GpuBigInt value)
    {
        var magnitude = value.Value.ToBigInteger();
        return value.IsNegative ? -magnitude : magnitude;
    }

    private GpuBigInt[] CreateInts(int numWords, params long[] values)
    {
        var array = new GpuBigInt[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var isNegative = values[i] < 0;
            var magnitude = (ulong)(isNegative ? -values[i] : values[i]);
            var words = new uint[numWords];
            words[0] = (uint)magnitude;
            if (numWords > 1)
            {
                words[1] = (uint)(magnitude >> 32);
            }

            array[i] = new GpuBigInt(new GpuBigUInt(_context, words), isNegative);
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

    // The floating-point inputs have a one-word significand, so that the first sum of 2^200 + 1 rounds.
    private GpuBigFloat CreateFloat(long value, int exponent = 0)
    {
        var isNegative = value < 0;
        var magnitude = (uint)(isNegative ? -value : value);
        return new GpuBigFloat(new GpuBigUInt(_context, [magnitude]), exponent, isNegative);
    }
}
