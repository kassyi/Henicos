#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// The random trees of VectorEvaluationOrderTests: their inputs, their builder and the leaves and scalars that record
// when they are read
public sealed partial class VectorEvaluationOrderTests
{
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
            return choice switch
            {
                0 => IntLeaf(capacity),
                1 or 2 or 3 => IntScalarStep(choice, depth, capacity),
                4 => IntDivided(depth, capacity),
                5 or 6 => IntZip(choice == 5, depth, capacity),
                7 when capacity < Elements => IntShared(depth, capacity),
                7 or 8 => IntTake(depth, capacity),
                9 when capacity < Elements => IntSkip(depth, capacity),
                9 => IntTakeAbove(depth, capacity),
                _ => IntWhere(depth, capacity)
            };
        }

        private (IGpuBigIntVectorExpression Node, string Shape) IntLeaf(int capacity)
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

        private (IGpuBigIntVectorExpression Node, string Shape) IntScalarStep(int choice, int depth, int capacity)
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

        private (IGpuBigIntVectorExpression Node, string Shape) IntDivided(int depth, int capacity)
        {
            var (operand, shape) = Int(depth - 1, capacity);
            var divisor = (uint)random.Next(1, 4);
            return (operand.SelectDivideByUInt32(divisor), $"/{divisor}({shape})");
        }

        private (IGpuBigIntVectorExpression Node, string Shape) IntZip(bool add, int depth, int capacity)
        {
            var (left, leftShape) = Int(depth - 1, capacity);
            var (right, rightShape) = Int(depth - 1, OtherCapacity(capacity));
            return add
                ? (left.ZipAdd(right), $"zip+({leftShape}, {rightShape})")
                : (left.ZipSubtract(right), $"zip-({leftShape}, {rightShape})");
        }

        // One sub-expression read by both inputs at different windows
        private (IGpuBigIntVectorExpression Node, string Shape) IntShared(int depth, int capacity)
        {
            var skip = random.Next(1, Elements - capacity + 1);
            var (shared, shape) = Int(depth - 1, capacity + skip);
            return (shared.Skip(skip).ZipSubtract(shared.Take(capacity)),
                $"shared({shape}).Skip({skip}) zip- .Take({capacity})");
        }

        // Take of an input as wide (a count at or above the capacity) or wider
        private (IGpuBigIntVectorExpression Node, string Shape) IntTake(int depth, int capacity)
        {
            var wider = random.Next(capacity, Elements + 1);
            var (operand, shape) = Int(depth - 1, wider);
            var count = wider > capacity ? capacity : capacity + random.Next(2);
            return (operand.Take(count), $"Take{count}({shape})");
        }

        private (IGpuBigIntVectorExpression Node, string Shape) IntSkip(int depth, int capacity)
        {
            var count = random.Next(1, Elements - capacity + 1);
            var (operand, shape) = Int(depth - 1, capacity + count);
            return (operand.Skip(count), $"Skip{count}({shape})");
        }

        private (IGpuBigIntVectorExpression Node, string Shape) IntTakeAbove(int depth, int capacity)
        {
            var (operand, shape) = Int(depth - 1, capacity);
            return (operand.Take(capacity + 1), $"Take{capacity + 1}({shape})");
        }

        private (IGpuBigIntVectorExpression Node, string Shape) IntWhere(int depth, int capacity)
        {
            var (source, shape) = Int(depth - 1, capacity);
            var (predicate, predicateShape) = Bool(depth - 1, capacity);
            return (source.Where(predicate), $"Where({shape}, {predicateShape})");
        }

        public (IGpuBoolVectorExpression Node, string Shape) Bool(int depth, int capacity)
        {
            var choice = depth <= 1 ? 0 : random.Next(5);
            return choice switch
            {
                0 => BoolCompareScalar(depth, capacity),
                1 => BoolCompareVector(depth, capacity),
                2 or 3 => BoolLogical(choice == 2, depth, capacity),
                _ => BoolNot(depth, capacity)
            };
        }

        private (IGpuBoolVectorExpression Node, string Shape) BoolCompareScalar(int depth, int capacity)
        {
            var (operand, shape) = Int(Math.Max(depth - 1, 0), capacity);
            var scalar = Scalar();
            var op = (VectorCompareOperator)random.Next(6);
            return (new VectorCompareScalarExpression(operand, scalar, op), $"{op}({shape}, {scalar.Id})");
        }

        private (IGpuBoolVectorExpression Node, string Shape) BoolCompareVector(int depth, int capacity)
        {
            var (left, leftShape) = Int(depth - 1, capacity);
            var (right, rightShape) = Int(depth - 1, OtherCapacity(capacity));
            var op = (VectorCompareOperator)random.Next(6);
            return (new VectorCompareVectorExpression(left, right, op), $"{op}({leftShape}, {rightShape})");
        }

        private (IGpuBoolVectorExpression Node, string Shape) BoolLogical(bool and, int depth, int capacity)
        {
            var (left, leftShape) = Bool(depth - 1, capacity);
            var (right, rightShape) = Bool(depth - 1, OtherCapacity(capacity));
            return and
                ? (left.And(right), $"And({leftShape}, {rightShape})")
                : (left.Or(right), $"Or({leftShape}, {rightShape})");
        }

        private (IGpuBoolVectorExpression Node, string Shape) BoolNot(int depth, int capacity)
        {
            var (operand, shape) = Bool(depth - 1, capacity);
            return (operand.Not(), $"Not({shape})");
        }

        public (IGpuBigFloatVectorExpression Node, string Shape) Float(int depth, int capacity)
        {
            var choice = depth == 0 ? 0 : random.Next(8);
            return choice switch
            {
                0 => FloatLeaf(capacity),
                1 or 2 => FloatScalarStep(choice == 1, depth, capacity),
                3 => FloatZip(depth, capacity),
                4 => FloatTake(depth, capacity),
                5 when capacity < Elements => FloatSkip(depth, capacity),
                5 or 6 => FloatDivided(depth, capacity),
                _ => FloatWhere(depth, capacity)
            };
        }

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatLeaf(int capacity)
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

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatScalarStep(bool add, int depth, int capacity)
        {
            var (operand, shape) = Float(depth - 1, capacity);
            var index = random.Next(inputs.FloatScalars.Count);
            var scalar = inputs.FloatScalars[index];
            return add
                ? (operand.SelectAdd(scalar), $"+c{index}({shape})")
                : (operand.SelectMultiply(scalar), $"*c{index}({shape})");
        }

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatZip(int depth, int capacity)
        {
            var (left, leftShape) = Float(depth - 1, capacity);
            var (right, rightShape) = Float(depth - 1, OtherCapacity(capacity));
            return (left.ZipSubtract(right), $"zip-({leftShape}, {rightShape})");
        }

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatTake(int depth, int capacity)
        {
            var wider = random.Next(capacity, Elements + 1);
            var (operand, shape) = Float(depth - 1, wider);
            var count = wider > capacity ? capacity : capacity + random.Next(2);
            return (operand.Take(count), $"Take{count}({shape})");
        }

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatSkip(int depth, int capacity)
        {
            var count = random.Next(1, Elements - capacity + 1);
            var (operand, shape) = Float(depth - 1, capacity + count);
            return (operand.Skip(count), $"Skip{count}({shape})");
        }

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatDivided(int depth, int capacity)
        {
            var (operand, shape) = Float(depth - 1, capacity);
            var divisor = (uint)random.Next(1, 4);
            return (operand.SelectDivideByUInt32(divisor), $"/{divisor}({shape})");
        }

        private (IGpuBigFloatVectorExpression Node, string Shape) FloatWhere(int depth, int capacity)
        {
            var (source, shape) = Float(depth - 1, capacity);
            var (compared, comparedShape) = Float(depth - 1, capacity);
            var index = random.Next(inputs.FloatScalars.Count);
            var predicate = compared.IsGreaterThan(inputs.FloatScalars[index]);
            return (source.Where(predicate), $"Where({shape}, GreaterThan({comparedShape}, c{index}))");
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
