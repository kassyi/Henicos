#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of deep unsigned expressions, built in a loop, against a BigInteger oracle computed from the host inputs:
// - evaluating such an expression does not take stack space per level: 100000 levels run on a thread with a 256 KB
//   stack under the CPU fallback (20000 on the GPU, with a 1 MB stack), where a walk that recurses once per level ends
//   the process with a stack overflow after about 120 levels (560 with 1 MB);
// - a deep chain and a balanced tree over the same leaves give the same value, and an order-dependent chain gives the
//   value of the oracle;
// - an evaluation that fails (a difference below zero, a Fit that does not fit, under the CPU fallback) returns every
//   buffer it leased: the live buffers of the workspace are the same before and after, also when the failing node is
//   deep in the chain.

/// <summary>Deep unsigned expressions under the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class UnsignedDeepChainTests
{
    private const int Depth = 100_000;
    private const int SmallStack = 256 * 1024;

    // e = (e + x_i).Truncate(2): every level is the left operand of the next, and the width stays at two words
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/BigIntExpressionVisitor.cs Walk -> recurse
    // at every level (`_depth < RecursionLimit` -> `true`; the process ends with a stack overflow)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LeftDeepChain_OfTruncatedSums_IsExact() =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, static workspace =>
            UnsignedChains.TruncatedSums(workspace, Depth, leftDeep: true));

    // e = (x_i + e).Truncate(2): every level is the right operand of the next
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RightDeepChain_OfTruncatedSums_IsExact() =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, static workspace =>
            UnsignedChains.TruncatedSums(workspace, Depth, leftDeep: false));

    // e = (((e << 7) >> 7) + x_i).Fit(3): shifts and a checked narrowing between the sums
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DeepChain_OfShiftsAndFits_IsExact() =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, static workspace =>
            UnsignedChains.ShiftsAndFits(workspace, Depth));

    // The sum of 1000 leaves as a left-deep chain and as a balanced tree: the same value, the oracle's
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/ExpressionWalk.cs Evaluate ->
    // `GetOperand(frame.Node, frame.OperandCount - 1)`: the right operand for both
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DeepChain_AndBalancedTree_OfTheSameLeaves_Agree() =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, static workspace =>
            UnsignedChains.DeepAndBalancedSums(workspace, 1000));

    // e = e * 3 - x_i: the operands of the difference cannot be swapped
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/ExpressionWalk.cs Evaluate -> a binary
    // node applied to `(second, first)`
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DeepChain_OfProductsAndDifferences_IsExact() =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, static workspace =>
            UnsignedChains.ProductsAndDifferences(workspace, 1000));

    // A failing root: the buffer Evaluate rented for the result goes back
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/GpuBigUIntExpressionExtensions.cs Evaluate ->
    // without the release of the rented result when the evaluation throws
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Scenario(TestScenario.SadPath)]
    public void FailingRoot_ReturnsEveryBuffer(bool intoTarget) =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, workspace =>
            UnsignedChains.FailureReturnsEveryBuffer(workspace, failAtRoot: true, intoTarget));

    // A failing node below the root: its result buffer and the operands evaluated before it go back
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/BigIntExpressionVisitor.cs Recurse -> without
    // the release of the result buffer when the operation throws
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Scenario(TestScenario.SadPath)]
    public void FailingInnerNode_ReturnsEveryBuffer(bool intoTarget) =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, workspace =>
            UnsignedChains.FailureReturnsEveryBuffer(workspace, failAtRoot: false, intoTarget));

    // A Fit that does not fit, halfway down a chain of 100000 levels: every buffer leased so far goes back
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/ExpressionWalk.cs Evaluate -> without the
    // release of the evaluated operands on a failure
    // mutant: BigIntExpressionVisitor.cs Apply (the walked levels) -> without the release of the result buffer when the
    // operation throws
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FailureHalfwayDownADeepChain_ReturnsEveryBuffer() =>
        UnsignedChains.RunOnStack(SmallStack, cpuFallback: true, static workspace =>
            UnsignedChains.DeepFailureReturnsEveryBuffer(workspace, Depth));
}

/// <summary>Deep unsigned expressions on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class UnsignedDeepChainGpuTests
{
    private const int Depth = 20_000;
    private const int StackSize = 1 << 20;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LeftDeepChain_OfTruncatedSums_IsExact() =>
        UnsignedChains.RunOnStack(StackSize, cpuFallback: false, static workspace =>
            UnsignedChains.TruncatedSums(workspace, Depth, leftDeep: true));

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RightDeepChain_OfTruncatedSums_IsExact() =>
        UnsignedChains.RunOnStack(StackSize, cpuFallback: false, static workspace =>
            UnsignedChains.TruncatedSums(workspace, Depth, leftDeep: false));

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DeepChain_OfShiftsAndFits_IsExact() =>
        UnsignedChains.RunOnStack(StackSize, cpuFallback: false, static workspace =>
            UnsignedChains.ShiftsAndFits(workspace, Depth));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DeepChain_AndBalancedTree_OfTheSameLeaves_Agree() =>
        UnsignedChains.RunOnStack(StackSize, cpuFallback: false, static workspace =>
            UnsignedChains.DeepAndBalancedSums(workspace, 1000));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DeepChain_OfProductsAndDifferences_IsExact() =>
        UnsignedChains.RunOnStack(StackSize, cpuFallback: false, static workspace =>
            UnsignedChains.ProductsAndDifferences(workspace, 1000));
}

/// <summary>The chains and their oracle checks, shared by the CPU fallback and GPU tests.</summary>
internal static class UnsignedChains
{
    // The leaves of a long chain repeat: a leaf per level would only slow the test down
    private const int Distinct = 64;

    private static readonly BigInteger s_twoWords = BigInteger.One << 64;

    public static void TruncatedSums(GpuWorkspace workspace, int depth, bool leftDeep)
    {
        var values = Values(Distinct, seed: 1513);
        var leaves = Leaves(workspace, values);
        try
        {
            GpuBigUIntExpr e = leaves[0];
            var expected = values[0];
            for (var i = 1; i < depth; i++)
            {
                var leaf = leaves[i % Distinct];
                e = leftDeep ? (e + leaf).Truncate(2) : ((GpuBigUIntExpr)leaf + e).Truncate(2);
                expected = (expected + values[i % Distinct]) % s_twoWords;
            }

            Assert.Equal(2, e.Node.NumWords);
            using var result = e.Node.Evaluate(workspace);
            Assert.Equal(expected, result.ToBigInteger());
        }
        finally
        {
            Dispose(leaves);
        }
    }

    public static void ShiftsAndFits(GpuWorkspace workspace, int depth)
    {
        var values = Values(Distinct, seed: 1514);
        var leaves = Leaves(workspace, values);
        try
        {
            GpuBigUIntExpr e = leaves[0];
            var expected = values[0];
            for (var i = 1; i < depth; i++)
            {
                // Below 2^32 * depth, so the shifted value keeps every bit and Fit(3) never reports
                e = (((e << 7) >> 7) + leaves[i % Distinct]).Fit(3);
                expected += values[i % Distinct];
            }

            using var result = e.Node.Evaluate(workspace);
            Assert.Equal(expected, result.ToBigInteger());
            workspace.ThrowIfDeviceErrors();
        }
        finally
        {
            Dispose(leaves);
        }
    }

    public static void DeepAndBalancedSums(GpuWorkspace workspace, int count)
    {
        var values = Values(count, seed: 1515);
        var leaves = Leaves(workspace, values);
        try
        {
            GpuBigUIntExpr deep = leaves[0];
            for (var i = 1; i < count; i++)
            {
                deep += leaves[i];
            }

            var balanced = Balanced(leaves, 0, count);
            var expected = values.Aggregate(BigInteger.Zero, static (sum, v) => sum + v);

            using var deepResult = deep.Node.Evaluate(workspace);
            using var balancedResult = balanced.Node.Evaluate(workspace);
            Assert.Equal(expected, deepResult.ToBigInteger());
            Assert.Equal(expected, balancedResult.ToBigInteger());
        }
        finally
        {
            Dispose(leaves);
        }
    }

    public static void ProductsAndDifferences(GpuWorkspace workspace, int count)
    {
        var values = Values(count, seed: 1516);
        values[0] = BigInteger.One << 40;
        var leaves = Leaves(workspace, values);
        using var three = GpuBigUInt.FromBigInteger(workspace, 3, 1);
        try
        {
            GpuBigUIntExpr e = leaves[0];
            var expected = values[0];
            for (var i = 1; i < count; i++)
            {
                // e >= 2^40 > x_i, so e * 3 - x_i stays above e: no difference below zero
                e = e * three - leaves[i];
                expected = expected * 3 - values[i];
            }

            using var result = e.Node.Evaluate(workspace);
            Assert.Equal(expected, result.ToBigInteger());
            workspace.ThrowIfDeviceErrors();
        }
        finally
        {
            Dispose(leaves);
        }
    }

    // (1 + 1) - (2 + 2) * 2 is below zero; as an inner node it is the left operand of a sum
    public static void FailureReturnsEveryBuffer(GpuWorkspace workspace, bool failAtRoot, bool intoTarget)
    {
        using var one = GpuBigUInt.FromBigInteger(workspace, 1, 1);
        using var two = GpuBigUInt.FromBigInteger(workspace, 2, 1);
        GpuBigUIntExpr a = one;
        GpuBigUIntExpr b = two;
        var negative = (a + a) - (b + b) * b;
        var e = failAtRoot ? negative : negative + a;
        using var target = new GpuBigUInt(workspace, e.Node.NumWords + 1);
        var before = workspace.GetMemoryStatistics().LiveBuffers;

        Assert.Throws<OverflowException>(() =>
        {
            using var result = intoTarget ? e.Node.Evaluate(workspace, target) : e.Node.Evaluate(workspace);
        });

        Assert.Equal(before, workspace.GetMemoryStatistics().LiveBuffers);
    }

    // A right-deep chain whose levels add a sum of two leaves (a temporary, evaluated before the deeper levels) and whose
    // middle level narrows a value of two words to one with Fit, which does not fit: the temporaries of every level
    // above the failure are alive when it fails
    public static void DeepFailureReturnsEveryBuffer(GpuWorkspace workspace, int depth)
    {
        var values = Values(Distinct, seed: 1517);
        var leaves = Leaves(workspace, values);
        using var big = GpuBigUInt.FromBigInteger(workspace, BigInteger.One << 40, 2);
        try
        {
            GpuBigUIntExpr e = leaves[0];
            for (var i = 1; i < depth; i++)
            {
                GpuBigUIntExpr leaf = leaves[i % Distinct];
                e = i == depth / 2 ? ((GpuBigUIntExpr)big + e).Fit(1) : ((leaf + leaf) + e).Truncate(2);
            }

            var before = workspace.GetMemoryStatistics().LiveBuffers;
            Assert.Throws<OverflowException>(() =>
            {
                using var result = e.Node.Evaluate(workspace);
            });
            Assert.Equal(before, workspace.GetMemoryStatistics().LiveBuffers);
        }
        finally
        {
            Dispose(leaves);
        }
    }

    // Runs the body with its own workspace on a thread with the given stack, and rethrows what it threw
    public static void RunOnStack(int stackSize, bool cpuFallback, Action<GpuWorkspace> body)
    {
        if (!cpuFallback)
        {
            CudaCapability.EnsureGpuAvailable();
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            GpuKernelLoader.ForceCpuFallback = cpuFallback;
            GpuKernelLoader.CudaContextScope? scope = null;
            try
            {
                if (!cpuFallback)
                {
                    scope = new GpuKernelLoader.CudaContextScope();
                }

                using var workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
                body(workspace);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                scope?.Dispose();
                GpuKernelLoader.ForceCpuFallback = false;
            }
        }, stackSize);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static GpuBigUIntExpr Balanced(GpuBigUInt[] leaves, int start, int count) =>
        count == 1
            ? leaves[start]
            : Balanced(leaves, start, count / 2) + Balanced(leaves, start + count / 2, count - count / 2);

    // One-word values (fixed seed)
    private static BigInteger[] Values(int count, int seed)
    {
        var random = new Random(seed);
        var values = new BigInteger[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (uint)random.Next() ^ ((uint)random.Next(2) << 31);
        }

        return values;
    }

    private static GpuBigUInt[] Leaves(GpuWorkspace workspace, BigInteger[] values)
    {
        var leaves = new GpuBigUInt[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            leaves[i] = GpuBigUInt.FromBigInteger(workspace, values[i], 1);
        }

        return leaves;
    }

    private static void Dispose(GpuBigUInt[] leaves)
    {
        foreach (var leaf in leaves)
        {
            leaf.Dispose();
        }
    }
}
