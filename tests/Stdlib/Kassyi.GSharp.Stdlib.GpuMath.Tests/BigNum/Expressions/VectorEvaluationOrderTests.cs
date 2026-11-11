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
// or threw.

/// <summary>Evaluation order of vector expression trees on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class VectorEvaluationOrderTests
{
    private const int Trees = 48;
    private const int MaxDepth = 5;
    private const int Elements = 6;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task VectorEvaluation_RandomTrees_KeepsTheOrderOfLeavesAndScalars()
    {
        var recursive = RunOnGpu();
        return Verifier.Verify(recursive)
            .UseFastHashSnapshotName(nameof(VectorEvaluation_RandomTrees_KeepsTheOrderOfLeavesAndScalars));
    }

    private static string RunOnGpu()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
        return Traces(workspace);
    }

    private static string Traces(GpuWorkspace workspace)
    {
        var lines = new List<string>();
        for (var seed = 0; seed < Trees; seed++)
        {
            var random = new Random(1608 + seed);
            var trace = new List<string>();
            var before = workspace.GetMemoryStatistics().LiveBuffers;
            using (var inputs = new TreeInputs(workspace, random, trace))
            {
                var kind = seed % 3;
                var builder = new TreeBuilder(random, inputs);
                lines.Add($"tree {seed}:");
                switch (kind)
                {
                    case 0:
                        {
                            var (node, shape) = builder.Int(MaxDepth, builder.RootCapacity());
                            lines.Add("  " + shape);
                            EvaluateInt(workspace, random, node, trace);
                            Dispose(node, trace);
                            break;
                        }

                    case 1:
                        {
                            var (node, shape) = builder.Bool(MaxDepth, builder.RootCapacity());
                            lines.Add("  " + shape);
                            Record(trace, () => "flags " + Join(ReadFlags(workspace, node.Evaluate(workspace))));
                            Dispose(node, trace);
                            break;
                        }

                    default:
                        {
                            var (node, shape) = builder.Float(MaxDepth, builder.RootCapacity());
                            lines.Add("  " + shape);
                            Record(trace, () => "values " + DescribeFloats(node.Evaluate(workspace)));
                            Dispose(node, trace);
                            break;
                        }
                }
            }

            lines.AddRange(trace.Select(line => "  " + line));
            Assert.Equal(before, workspace.GetMemoryStatistics().LiveBuffers);
        }

        return string.Join('\n', lines);
    }

    // The whole vector, a window of it, or the whole vector into a target
    private static void EvaluateInt(GpuWorkspace workspace, Random random, IGpuBigIntVectorExpression node,
        List<string> trace)
    {
        var mode = random.Next(3);
        var capacity = node.Capacity;
        if (mode == 0 || capacity == 0)
        {
            Record(trace, () => "values " + Join(ReadInts(workspace, node.Evaluate(workspace))));
            return;
        }

        if (mode == 1)
        {
            var offset = capacity > 0 ? random.Next(capacity) : 0;
            var count = capacity - offset > 0 ? random.Next(capacity - offset + 1) : 0;
            Record(trace, () =>
                $"window {offset}+{count}: " + Join(ReadInts(workspace, node.Evaluate(workspace, offset, count))));
            return;
        }

        using var target = new GpuBigIntArray(workspace, capacity, node.WordsPerElement);
        Record(trace, () =>
        {
            var result = node.Evaluate(workspace, 0, null, target.Values);
            return "into target: " + Join(ReadInts(workspace, result));
        });
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

    private static void Dispose(IDisposable node, List<string> trace)
    {
        trace.Add("dispose:");
        node.Dispose();
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

    // The leaves and scalars of one tree: arrays and values chosen by the seed, each wrapped in a node that records
    // when it is evaluated and disposed
    private sealed class TreeInputs : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        public TreeInputs(GpuWorkspace workspace, Random random, List<string> trace)
        {
            Workspace = workspace;
            Trace = trace;
            for (var i = 0; i < 3; i++)
            {
                var values = new BigInteger[Elements];
                for (var e = 0; e < Elements; e++)
                {
                    values[e] = random.Next(-50, 50);
                }

                IntArrays.Add(Own(IntArray(workspace, values)));
                FloatArrays.Add(Own(FloatArray(workspace, values)));
                Scalars.Add(Own(new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, random.Next(1, 9), 1),
                    random.Next(2) == 0)));
                FloatScalars.Add(Own(GpuBigFloatTestHelpers.CreateFloat(workspace, random.Next(-9, 9))));
            }
        }

        public GpuWorkspace Workspace { get; }

        public List<string> Trace { get; }

        public List<GpuBigIntArray> IntArrays { get; } = [];

        public List<GpuBigFloatArray> FloatArrays { get; } = [];

        public List<GpuBigInt> Scalars { get; } = [];

        public List<GpuBigFloat> FloatScalars { get; } = [];

        public void Dispose()
        {
            foreach (var owned in _owned)
            {
                owned.Dispose();
            }
        }

        private T Own<T>(T item)
            where T : IDisposable
        {
            _owned.Add(item);
            return item;
        }

        private static GpuBigIntArray IntArray(GpuWorkspace workspace, BigInteger[] values)
        {
            var array = new GpuBigIntArray(workspace, values.Length, 1);
            var limbs = values.Select(v => (uint)BigInteger.Abs(v)).ToArray();
            var signs = values.Select(v => v < 0 ? 1u : 0u).ToArray();
            workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, limbs, limbs.Length);
            workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length);
            return array;
        }

        private static GpuBigFloatArray FloatArray(GpuWorkspace workspace, BigInteger[] values)
        {
            var array = new GpuBigFloatArray(workspace, values.Length, 4);
            for (var i = 0; i < values.Length; i++)
            {
                using var element = GpuBigFloatTestHelpers.CreateFloat(workspace, (long)values[i]);
                array.SetElement(i, element);
            }

            return array;
        }
    }

    // Random trees of every vector node kind, each node of the capacity its parent asks for (a Take or a Skip adjusts
    // it); one second input in twelve gets another capacity on purpose, so that the length checks are reached. The
    // shape of each tree is written into the snapshot.
    private sealed class TreeBuilder(Random random, TreeInputs inputs)
    {
        private int _ids;

        // The capacity of a root: mostly every element, sometimes fewer, sometimes none
        public int RootCapacity() => random.Next(4) == 0 ? random.Next(Elements) : Elements;

        public (IGpuBigIntVectorExpression Node, string Shape) Int(int depth, int capacity)
        {
            var choice = depth == 0 ? 0 : random.Next(11);
            switch (choice)
            {
                case 0:
                    {
                        var id = "i" + _ids++;
                        var array = inputs.IntArrays[random.Next(inputs.IntArrays.Count)];
                        IGpuBigIntVectorExpression leaf = new TracedIntLeaf(id, array, inputs.Trace);
                        if (capacity == Elements)
                        {
                            return (leaf, id);
                        }

                        return random.Next(2) == 0
                            ? (leaf.Take(capacity), $"Take{capacity}({id})")
                            : (leaf.Skip(Elements - capacity), $"Skip{Elements - capacity}({id})");
                    }

                case 1:
                case 2:
                case 3:
                    {
                        var (operand, shape) = Int(depth - 1, capacity);
                        var scalar = Scalar();
                        IGpuBigIntVectorExpression node = choice switch
                        {
                            1 => new VectorAddScalarExpression(operand, scalar),
                            2 => new VectorSubScalarExpression(operand, scalar),
                            _ => new VectorMulScalarExpression(operand, scalar)
                        };
                        return (node, $"{"+-*"[choice - 1]}{scalar.Id}({shape})");
                    }

                case 4:
                    {
                        var (operand, shape) = Int(depth - 1, capacity);
                        var divisor = (uint)random.Next(1, 4);
                        return (operand.SelectDivideByUInt32(divisor), $"/{divisor}({shape})");
                    }

                case 5:
                case 6:
                    {
                        var (left, leftShape) = Int(depth - 1, capacity);
                        var (right, rightShape) = Int(depth - 1, OtherCapacity(capacity));
                        return choice == 5
                            ? (left.ZipAdd(right), $"zip+({leftShape}, {rightShape})")
                            : (left.ZipSubtract(right), $"zip-({leftShape}, {rightShape})");
                    }

                case 7 when capacity < Elements:
                    {
                        // One sub-expression read by both inputs at different windows
                        var skip = random.Next(1, Elements - capacity + 1);
                        var (shared, shape) = Int(depth - 1, capacity + skip);
                        return (shared.Skip(skip).ZipSubtract(shared.Take(capacity)),
                            $"shared({shape}).Skip({skip}) zip- .Take({capacity})");
                    }

                case 7:
                case 8:
                    {
                        // Take of an input as wide (a count at or above the capacity) or wider
                        var wider = random.Next(capacity, Elements + 1);
                        var (operand, shape) = Int(depth - 1, wider);
                        var count = wider > capacity ? capacity : capacity + random.Next(2);
                        return (operand.Take(count), $"Take{count}({shape})");
                    }

                case 9 when capacity < Elements:
                    {
                        var count = random.Next(1, Elements - capacity + 1);
                        var (operand, shape) = Int(depth - 1, capacity + count);
                        return (operand.Skip(count), $"Skip{count}({shape})");
                    }

                case 9:
                    {
                        var (operand, shape) = Int(depth - 1, capacity);
                        return (operand.Take(capacity + 1), $"Take{capacity + 1}({shape})");
                    }

                default:
                    {
                        var (source, shape) = Int(depth - 1, capacity);
                        var (predicate, predicateShape) = Bool(depth - 1, capacity);
                        return (source.Where(predicate), $"Where({shape}, {predicateShape})");
                    }
            }
        }

        public (IGpuBoolVectorExpression Node, string Shape) Bool(int depth, int capacity)
        {
            var choice = depth <= 1 ? 0 : random.Next(5);
            switch (choice)
            {
                case 0:
                    {
                        var (operand, shape) = Int(Math.Max(depth - 1, 0), capacity);
                        var scalar = Scalar();
                        var op = (VectorCompareOperator)random.Next(6);
                        return (new VectorCompareScalarExpression(operand, scalar, op), $"{op}({shape}, {scalar.Id})");
                    }

                case 1:
                    {
                        var (left, leftShape) = Int(depth - 1, capacity);
                        var (right, rightShape) = Int(depth - 1, OtherCapacity(capacity));
                        var op = (VectorCompareOperator)random.Next(6);
                        return (new VectorCompareVectorExpression(left, right, op), $"{op}({leftShape}, {rightShape})");
                    }

                case 2:
                case 3:
                    {
                        var (left, leftShape) = Bool(depth - 1, capacity);
                        var (right, rightShape) = Bool(depth - 1, OtherCapacity(capacity));
                        return choice == 2
                            ? (left.And(right), $"And({leftShape}, {rightShape})")
                            : (left.Or(right), $"Or({leftShape}, {rightShape})");
                    }

                default:
                    {
                        var (operand, shape) = Bool(depth - 1, capacity);
                        return (operand.Not(), $"Not({shape})");
                    }
            }
        }

        public (IGpuBigFloatVectorExpression Node, string Shape) Float(int depth, int capacity)
        {
            var choice = depth == 0 ? 0 : random.Next(8);
            switch (choice)
            {
                case 0:
                    {
                        var id = "f" + _ids++;
                        var array = inputs.FloatArrays[random.Next(inputs.FloatArrays.Count)];
                        IGpuBigFloatVectorExpression leaf = new TracedFloatLeaf(id, array, inputs.Trace);
                        if (capacity == Elements)
                        {
                            return (leaf, id);
                        }

                        return random.Next(2) == 0
                            ? (leaf.Take(capacity), $"Take{capacity}({id})")
                            : (leaf.Skip(Elements - capacity), $"Skip{Elements - capacity}({id})");
                    }

                case 1:
                case 2:
                    {
                        var (operand, shape) = Float(depth - 1, capacity);
                        var index = random.Next(inputs.FloatScalars.Count);
                        var scalar = inputs.FloatScalars[index];
                        return choice == 1
                            ? (operand.SelectAdd(scalar), $"+c{index}({shape})")
                            : (operand.SelectMultiply(scalar), $"*c{index}({shape})");
                    }

                case 3:
                    {
                        var (left, leftShape) = Float(depth - 1, capacity);
                        var (right, rightShape) = Float(depth - 1, OtherCapacity(capacity));
                        return (left.ZipSubtract(right), $"zip-({leftShape}, {rightShape})");
                    }

                case 4:
                    {
                        var wider = random.Next(capacity, Elements + 1);
                        var (operand, shape) = Float(depth - 1, wider);
                        var count = wider > capacity ? capacity : capacity + random.Next(2);
                        return (operand.Take(count), $"Take{count}({shape})");
                    }

                case 5 when capacity < Elements:
                    {
                        var count = random.Next(1, Elements - capacity + 1);
                        var (operand, shape) = Float(depth - 1, capacity + count);
                        return (operand.Skip(count), $"Skip{count}({shape})");
                    }

                case 5:
                case 6:
                    {
                        var (operand, shape) = Float(depth - 1, capacity);
                        var divisor = (uint)random.Next(1, 4);
                        return (operand.SelectDivideByUInt32(divisor), $"/{divisor}({shape})");
                    }

                default:
                    {
                        var (source, shape) = Float(depth - 1, capacity);
                        var (compared, comparedShape) = Float(depth - 1, capacity);
                        var index = random.Next(inputs.FloatScalars.Count);
                        var predicate = compared.IsGreaterThan(inputs.FloatScalars[index]);
                        return (source.Where(predicate), $"Where({shape}, GreaterThan({comparedShape}, c{index}))");
                    }
            }
        }

        // The capacity of a second input: the first one's, except in one case of twelve
        private int OtherCapacity(int capacity) =>
            random.Next(12) == 0 ? (capacity + 1 + random.Next(Elements)) % (Elements + 1) : capacity;

        private TracedScalar Scalar()
        {
            var index = random.Next(inputs.Scalars.Count);
            return new TracedScalar("s" + _ids++ + "=" + index, inputs.Scalars[index], inputs.Trace);
        }
    }

    // An array leaf that records the window it is evaluated over, and its disposal
    private sealed class TracedIntLeaf(string id, GpuBigIntArray array, List<string> trace) : IGpuBigIntVectorExpression
    {
        private readonly IGpuBigIntVectorExpression _inner = array.AsVectorizedEnumerable();

        public int Length => _inner.Length;

        public int WordsPerElement => _inner.WordsPerElement;

        public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigUIntArrayView targetResult = default)
        {
            trace.Add(Window(id, elementOffset, elementCount, targetResult.Workspace is not null));
            return _inner.Evaluate(workspace, elementOffset, elementCount, targetResult);
        }

        public void Dispose()
        {
            trace.Add($"  {id} disposed");
            _inner.Dispose();
        }
    }

    // The same for a floating-point array
    private sealed class TracedFloatLeaf(string id, GpuBigFloatArray array, List<string> trace)
        : IGpuBigFloatVectorExpression
    {
        private readonly IGpuBigFloatVectorExpression _inner = array.AsVectorizedEnumerable();

        public int Length => _inner.Length;

        public int WordsPerElement => _inner.WordsPerElement;

        public GpuBigFloatArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
            GpuBigFloatArrayView targetResult = default)
        {
            trace.Add(Window(id, elementOffset, elementCount, targetResult.Workspace is not null));
            return _inner.Evaluate(workspace, elementOffset, elementCount, targetResult);
        }

        public void Dispose()
        {
            trace.Add($"  {id} disposed");
            _inner.Dispose();
        }
    }

    // The window a leaf is evaluated over, as the trace shows it
    private static string Window(string id, int offset, int? count, bool intoTarget) =>
        $"{id} {offset}+{count?.ToString() ?? "all"}{(intoTarget ? " into target" : "")}";

    // A scalar operand that records when a node reads it
    private sealed class TracedScalar(string id, GpuBigInt value, List<string> trace) : IGpuBigIntExpression
    {
        public string Id { get; } = id;

        public int NumWords => value.Value.NumWords;

        public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
        {
            trace.Add(Id);
            return new GpuBigInt(value.Value.AsBorrowed(), value.IsNegative);
        }
    }
}
