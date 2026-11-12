#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// The order in which the evaluation of a vector expression reads its leaves and scalars, recorded on random expression
// trees (fixed seeds) of every vector node kind, with the values or the exception of each evaluation and the order in
// which Dispose reaches the leaves, compared with a snapshot. The snapshot was taken with the recursive evaluation,
// before the walk of the trees moved to an explicit stack; the walk must keep:
// - the inputs of a node evaluated before the node, in the order the node reads them (the predicate of a Where before
//   its source), each over the window the node asks for (Skip, Take, a caller's offset, count and target);
// - the checks of a node (a caller's target, the lengths of a Zip) made before any input is evaluated;
// - a sub-expression shared by both inputs of a node evaluated again when the second evaluation overwrote the first;
// - Dispose reaching the inputs of a node in the order the node released them.
// Every tree leaves the workspace with the buffers it had before the tree was built, whether its evaluation succeeded
// or threw. The trees are evaluated by recursion (they are shallower than the levels VectorWalk recurses through),
// walked whole, and walked below their second level: the three traces are the same.

/// <summary>Evaluation order of vector expression trees on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed partial class VectorEvaluationOrderTests
{
    private const int Trees = 48;
    private const int MaxDepth = 5;
    private const int Elements = 6;

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/VectorWalk.cs Walker.Enter -> no check (the
    // walked trees evaluate inputs before a length check throws)
    // mutant: VectorCompareExpressions.cs VectorCompareVectorExpression.EvaluateDirect -> without `Enter(request)`
    // mutant: VectorBinaryVectorExpressions.cs EvaluateDirect (recursion) -> the right input evaluated as the first
    // mutant: VectorWalk.cs Walker.OperandTarget -> the node's own request
    // mutant: ExpressionDisposal.cs Dispose -> without pushing the inputs (leaves below the root are not disposed)
    // mutant: VectorWhereExpression.cs GetOperand -> the source before the predicate
    // mutant: VectorWhereExpression.cs GetInput -> the predicate released before the source
    // mutant: VectorSliceExpressions.cs VectorTakeExpression.EvaluatesOperands -> `true` (an empty window evaluates
    // the source)
    // mutant: VectorOperandPair.cs KeepBoth -> never evaluates the left input again (a shared sub-expression)
    // mutant: VectorSliceExpressions.cs VectorTakeExpression.EvaluateDirect -> without the EvaluatesOperands check
    // (EvaluateOnCallStack evaluates the source of an empty window)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task VectorEvaluation_RandomTrees_KeepsTheOrderOfLeavesAndScalars()
    {
        // Through the public Evaluate (recursion for these depths), walked whole, walked below the second level, and by
        // the recursion of EvaluateOnCallStack
        var recursive = RunOnGpu(levels: null);
        var walked = RunOnGpu(levels: 0);
        var walkedBelowTwo = RunOnGpu(levels: 2);
        var onCallStack = RunOnGpu(levels: OnCallStack);
        for (var tree = 0; tree < Trees; tree++)
        {
            Assert.Equal(recursive[tree], walked[tree]);
            Assert.Equal(recursive[tree], walkedBelowTwo[tree]);
            Assert.Equal(recursive[tree], onCallStack[tree]);
        }

        return Verifier.Verify(string.Join('\n', recursive))
            .UseFastHashSnapshotName(nameof(VectorEvaluation_RandomTrees_KeepsTheOrderOfLeavesAndScalars));
    }

    // The levels argument for the evaluation through VectorExpressionRecursion.EvaluateOnCallStack
    private const int OnCallStack = -1;

    // The trace of each tree, evaluated with the given number of levels by recursion (null: the public Evaluate;
    // OnCallStack: EvaluateOnCallStack)
    private static string[] RunOnGpu(int? levels)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
        var traces = new string[Trees];
        for (var seed = 0; seed < Trees; seed++)
        {
            traces[seed] = Tree(workspace, seed, levels);
        }

        return traces;
    }

    // One tree: its shape, the leaves and scalars in the order they were read, the result or the exception, and the
    // order Dispose reached the leaves
    private static string Tree(GpuWorkspace workspace, int seed, int? levels)
    {
        var random = new Random(1608 + seed);
        var trace = new List<string>();
        var lines = new List<string> { $"tree {seed}:" };
        var before = workspace.GetMemoryStatistics().LiveBuffers;
        using (var inputs = new TreeInputs(workspace, random, trace))
        {
            var builder = new TreeBuilder(random, inputs);
            var evaluator = new TreeEvaluator(workspace, levels, trace);
            IDisposable node = (seed % 3) switch
            {
                0 => evaluator.Int(random, builder.Int(MaxDepth, builder.RootCapacity()), lines),
                1 => evaluator.Bool(builder.Bool(MaxDepth, builder.RootCapacity()), lines),
                _ => evaluator.Float(builder.Float(MaxDepth, builder.RootCapacity()), lines)
            };
            trace.Add("dispose:");
            node.Dispose();
        }

        lines.AddRange(trace.Select(line => "  " + line));
        Assert.Equal(before, workspace.GetMemoryStatistics().LiveBuffers);
        return string.Join('\n', lines);
    }

    // Evaluates a root and records the result: through the public Evaluate, or through VectorWalk with a given number
    // of levels evaluated by recursion
    private sealed class TreeEvaluator(GpuWorkspace workspace, int? levels, List<string> trace)
    {
        // The whole vector, a window of it, or the whole vector into a target
        public IDisposable Int(Random random, (IGpuBigIntVectorExpression Node, string Shape) tree, List<string> lines)
        {
            var node = tree.Node;
            lines.Add("  " + tree.Shape);
            var mode = random.Next(3);
            var capacity = node.Capacity;
            if (mode == 0 || capacity == 0)
            {
                Record(trace, () => "values " + Join(ReadInts(workspace, Evaluate(node, 0, null, default))));
                return node;
            }

            if (mode == 1)
            {
                var offset = random.Next(capacity);
                var count = random.Next(capacity - offset + 1);
                Record(trace, () =>
                    $"window {offset}+{count}: " + Join(ReadInts(workspace, Evaluate(node, offset, count, default))));
                return node;
            }

            using var target = new GpuBigIntArray(workspace, capacity, node.WordsPerElement);
            Record(trace, () => "into target: " + Join(ReadInts(workspace, Evaluate(node, 0, null, target.Values))));
            return node;
        }

        public IDisposable Bool((IGpuBoolVectorExpression Node, string Shape) tree, List<string> lines)
        {
            lines.Add("  " + tree.Shape);
            Record(trace, () => "flags " + Join(ReadFlags(workspace, Evaluate(tree.Node))));
            return tree.Node;
        }

        public IDisposable Float((IGpuBigFloatVectorExpression Node, string Shape) tree, List<string> lines)
        {
            lines.Add("  " + tree.Shape);
            Record(trace, () => "values " + DescribeFloats(Evaluate(tree.Node)));
            return tree.Node;
        }

        private GpuBigIntArrayView Evaluate(IGpuBigIntVectorExpression node, int offset, int? count,
            GpuBigUIntArrayView target)
        {
            if (levels == OnCallStack)
            {
                return node.EvaluateOnCallStack(workspace, offset, count, target);
            }

            if (levels is not { } limit || node is not IVectorOperator op)
            {
                return node.Evaluate(workspace, offset, count, target);
            }

            var request = new VectorRequest(offset, count, VectorStorage.Of(target));
            return DeviceCounted.Materialize(workspace, VectorWalk.Evaluate(op, workspace, request, limit).Int);
        }

        private GpuBigUIntArrayView Evaluate(IGpuBoolVectorExpression node) =>
            levels == OnCallStack ? node.EvaluateOnCallStack(workspace) :
            levels is { } limit && node is IVectorOperator op
                ? DeviceCounted.Materialize(workspace,
                    VectorWalk.Evaluate(op, workspace, VectorRequest.Whole, limit).Bool)
                : node.Evaluate(workspace);

        private GpuBigFloatArrayView Evaluate(IGpuBigFloatVectorExpression node) =>
            levels == OnCallStack ? node.EvaluateOnCallStack(workspace) :
            levels is { } limit && node is IVectorOperator op
                ? DeviceCounted.Materialize(workspace,
                    VectorWalk.Evaluate(op, workspace, VectorRequest.Whole, limit).Float)
                : node.Evaluate(workspace);
    }

    private static void Record(List<string> trace, Func<string> evaluate)
    {
        try
        {
            trace.Add("= " + evaluate());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException
            or DivideByZeroException)
        {
            trace.Add($"! {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Join<T>(IEnumerable<T> values) => "[" + string.Join(", ", values) + "]";

    private static BigInteger[] ReadInts(GpuWorkspace workspace, GpuBigIntArrayView view)
    {
        var words = view.WordsPerElement;
        var limbs = new uint[view.Length * words];
        var signs = new uint[view.Length];
        if (view.Length > 0)
        {
            workspace.Provider.CopyDeviceToHost(limbs, view.Values.DevicePointer, limbs.Length);
            workspace.Provider.CopyDeviceToHost(signs, view.Signs.DevicePointer, signs.Length);
        }

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
        if (view.Length > 0)
        {
            workspace.Provider.CopyDeviceToHost(flags, view.DevicePointer, flags.Length);
        }

        return flags;
    }

    private static string DescribeFloats(GpuBigFloatArrayView view)
    {
        var parts = new string[view.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var element = view.GetElement(i);
            parts[i] = BigFloatReference.Describe(element.Significand.ToBigInteger(), element.Exponent,
                element.IsNegative);
        }

        return Join(parts);
    }
}
