#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of deep signed expressions, built in a loop as `e = x + e` (deep along the right operand), with negations
// and Fit between the levels, against a BigInteger oracle computed from the host inputs:
// - the value and the width of the result are exact;
// - building, reading the width, evaluating and disposing such an expression does not take stack space per level: a
//   chain of 20000 levels runs on a thread with a 1 MB stack (the default size), where a walk that recurses once per
//   level ends the process with a stack overflow.

/// <summary>Deep signed expressions on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class SignedDeepChainTests
{
    private const int Depth = 20000;
    private const int StackSize = 1 << 20;

    // e = x_i + e: every level is the right operand of the next
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/SignedExpressions.cs SignedTree.Evaluate ->
    // `binary.Right.Evaluate(workspace)` for the right operand (the process ends with a stack overflow)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RightDeepSum_IsExact() =>
        RunOnSmallStack(static workspace =>
        {
            var values = Values(Depth);
            var leaves = Leaves(workspace, values);
            try
            {
                GpuBigIntExpr e = leaves[0];
                var expected = values[0];
                for (var i = 1; i < Depth; i++)
                {
                    e = leaves[i] + e;
                    expected = values[i] + expected;
                }

                // Every addition adds one bit to the bound of the 32-bit leaves
                Assert.Equal(BigIntWidths.Words(32L + Depth - 1), e.Node.NumWords);
                using var result = e.Evaluate(workspace);
                Assert.Equal(expected, Read(result));
                Assert.Equal(e.Node.NumWords, result.Value.NumWords);
                e.Dispose();
            }
            finally
            {
                Dispose(leaves);
            }
        });

    // e = -(x_i - e).Fit(2): a negation, a checked narrowing and a subtraction whose sign depends on the values at
    // every level; the magnitude stays below 2^32 * 2, so Fit never reports
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/SignedExpressions.cs SignedTree.Evaluate ->
    // `negate.Target.Evaluate(workspace, target)` for a negation (the process ends with a stack overflow)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RightDeepChain_OfNegationsFitsAndDifferences_IsExact() =>
        RunOnSmallStack(static workspace =>
        {
            var values = Values(Depth);
            var leaves = Leaves(workspace, values);
            try
            {
                GpuBigIntExpr e = leaves[0];
                var expected = values[0];
                for (var i = 1; i < Depth; i++)
                {
                    e = -(leaves[i] - e).Fit(2);
                    expected = -(values[i] - expected);
                }

                Assert.Equal(2, e.Node.NumWords);
                using var result = e.Evaluate(workspace);
                Assert.Equal(expected, Read(result));
                workspace.ThrowIfDeviceErrors();
                e.Dispose();
            }
            finally
            {
                Dispose(leaves);
            }
        });

    // A deep right chain into a caller's target: the target is checked and cleared above the result once, at the root
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RightDeepSum_IntoAWiderTarget_ClearsTheWordsAbove() =>
        RunOnSmallStack(static workspace =>
        {
            var values = Values(Depth);
            var leaves = Leaves(workspace, values);
            try
            {
                GpuBigIntExpr e = leaves[0];
                var expected = values[0];
                for (var i = 1; i < Depth; i++)
                {
                    e = -(leaves[i] + e);
                    expected = -(values[i] + expected);
                }

                var words = e.Node.NumWords;
                using var narrow = new GpuBigUInt(workspace, words - 1);
                Assert.Throws<ArgumentException>(() => e.Evaluate(narrow));

                using var target = new GpuBigUInt(workspace, Enumerable.Repeat(0xDEADBEEFu, words + 3).ToArray());
                var result = e.Evaluate(target);
                Assert.Equal(expected, Read(result));
                var limbs = new uint[words + 3];
                BigInteger.Abs(expected).WriteLimbs(limbs);
                Assert.Equal(limbs, target.ToHostArray());
            }
            finally
            {
                Dispose(leaves);
            }
        });

    // Small values of both signs, so that the differences change sign along the chain
    private static BigInteger[] Values(int count)
    {
        var random = new Random(1423);
        var values = new BigInteger[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = random.Next(-1000, 1000);
        }

        return values;
    }

    private static GpuBigInt[] Leaves(GpuWorkspace workspace, BigInteger[] values)
    {
        var leaves = new GpuBigInt[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var limbs = new uint[1];
            BigInteger.Abs(values[i]).WriteLimbs(limbs);
            leaves[i] = new GpuBigInt(new GpuBigUInt(workspace, limbs), values[i].Sign < 0);
        }

        return leaves;
    }

    private static void Dispose(GpuBigInt[] leaves)
    {
        foreach (var leaf in leaves)
        {
            leaf.Dispose();
        }
    }

    private static BigInteger Read(GpuBigInt value)
    {
        var magnitude = value.Value.ToBigInteger();
        return value.IsNegative ? -magnitude : magnitude;
    }

    // Runs the body with its own workspace on a thread whose stack is 1 MB, and rethrows what it threw
    private static void RunOnSmallStack(Action<GpuWorkspace> body)
    {
        CudaCapability.EnsureGpuAvailable();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var scope = new GpuKernelLoader.CudaContextScope();
                using var workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
                body(workspace);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }, StackSize);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
