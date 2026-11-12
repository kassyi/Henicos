#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of EvaluateOnCallStack, the evaluation by plain recursion:
// - an expression of MaxCallStackDepth levels evaluates on a thread with a 256 KB stack, with the values of Evaluate
//   and of a BigInteger oracle (integer, floating-point and flag vectors);
// - one level more throws NotSupportedException, and Evaluate still evaluates that expression.
// The order of evaluation against the walk is checked on the random trees of VectorEvaluationOrderTests.

/// <summary>The low-level recursive evaluation of vector expressions, on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class VectorExpressionRecursionTests
{
    private const int SmallStack = 256 * 1024;
    private const int MaxDepth = VectorExpressionRecursion.MaxCallStackDepth;

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/RecursionLevel.cs Recurses -> `Depth <=
    // Limit` (one level more is evaluated)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IntChain_AtAndBeyondTheMaximumDepth_EvaluatesOrThrows() =>
        LimitedStack.Run(SmallStack, cpuFallback: false, static workspace =>
        {
            BigInteger[] values = [5, 7, 11, 0];
            using var array = Ints(workspace, values);
            using var s = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 3, 1), false);
            var e = array.AsVectorizedEnumerable();
            try
            {
                for (var k = 0; k < MaxDepth; k++)
                {
                    e = e.SelectAdd(s);
                }

                var expected = values.Select(x => x + 3 * MaxDepth).ToArray();
                Assert.Equal(expected, ReadInts(workspace, e.EvaluateOnCallStack(workspace)));
                Assert.Equal(expected, ReadInts(workspace, e.Evaluate(workspace)));

                e = e.SelectAdd(s);
                Assert.Throws<NotSupportedException>(() => e.EvaluateOnCallStack(workspace));
                Assert.Equal(values.Select(x => x + 3 * (MaxDepth + 1)), ReadInts(workspace, e.Evaluate(workspace)));
            }
            finally
            {
                e.Dispose();
            }
        });

    // A Zip at every level, a window and a target: the inputs and the window as Evaluate passes them
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ZipChainIntoATarget_AtTheMaximumDepth_MatchesEvaluate() =>
        LimitedStack.Run(SmallStack, cpuFallback: false, static workspace =>
        {
            using var array = Ints(workspace, [5, 7, 11, 0]);
            using var other = Ints(workspace, [1, 2, 3, 4]);
            var e = array.AsVectorizedEnumerable();
            try
            {
                for (var k = 0; k < MaxDepth; k++)
                {
                    e = other.AsVectorizedEnumerable().ZipSubtract(e);
                }

                using var target = new GpuBigIntArray(workspace, 3, e.WordsPerElement);
                var recursive = ReadInts(workspace, e.EvaluateOnCallStack(workspace, 1, 3, target.Values));
                var walked = ReadInts(workspace, e.Evaluate(workspace, 1, 3, target.Values));
                Assert.Equal(walked, recursive);
                Assert.Equal(new BigInteger[] { 7, 11, 0 }, recursive);
            }
            finally
            {
                e.Dispose();
            }
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FlagAndFloatChains_AtTheMaximumDepth_MatchEvaluate() =>
        LimitedStack.Run(SmallStack, cpuFallback: false, static workspace =>
        {
            using var array = Ints(workspace, [5, 0, 11, 0]);
            using var zero = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 0, 1), false);
            var flags = array.AsVectorizedEnumerable().IsGreaterThan(zero);
            using var floats = new GpuBigFloatArray(workspace, 4, 4);
            var f = floats.AsVectorizedEnumerable();
            try
            {
                for (var k = 1; k < MaxDepth; k++)
                {
                    flags = flags.Not();
                    f = f.Take(4);
                }

                // 63 negations of x > 0
                Assert.Equal([0u, 1u, 0u, 1u], ReadFlags(workspace, flags.EvaluateOnCallStack(workspace)));
                Assert.Equal(4, f.EvaluateOnCallStack(workspace).Length);
                Assert.Throws<NotSupportedException>(() => flags.Not().EvaluateOnCallStack(workspace));
            }
            finally
            {
                flags.Dispose();
                f.Dispose();
            }
        });

    private static GpuBigIntArray Ints(GpuWorkspace workspace, uint[] values)
    {
        var array = new GpuBigIntArray(workspace, values.Length, 1);
        workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, values, values.Length);
        workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, new uint[values.Length], values.Length);
        return array;
    }

    private static GpuBigIntArray Ints(GpuWorkspace workspace, BigInteger[] values) =>
        Ints(workspace, values.Select(x => (uint)x).ToArray());

    private static BigInteger[] ReadInts(GpuWorkspace workspace, GpuBigIntArrayView view)
    {
        var words = view.WordsPerElement;
        var limbs = new uint[view.Length * words];
        var signs = new uint[view.Length];
        workspace.Provider.CopyDeviceToHost(limbs, view.Values.DevicePointer, limbs.Length);
        workspace.Provider.CopyDeviceToHost(signs, view.Signs.DevicePointer, signs.Length);
        var result = new BigInteger[view.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var magnitude = BigInteger.Zero;
            for (var w = words - 1; w >= 0; w--)
            {
                magnitude = (magnitude << 32) | limbs[i * words + w];
            }

            result[i] = signs[i] != 0 ? -magnitude : magnitude;
        }

        return result;
    }

    private static uint[] ReadFlags(GpuWorkspace workspace, GpuBigUIntArrayView view)
    {
        var flags = new uint[view.Length];
        workspace.Provider.CopyDeviceToHost(flags, view.DevicePointer, flags.Length);
        return flags;
    }
}
