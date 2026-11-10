#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// The order in which an evaluation reads its leaves, leases and returns its buffers and runs its operations, recorded on
// random expression trees (fixed seeds) under the CPU fallback and compared with a snapshot. The snapshots were taken
// with the recursive evaluation, before the walk of the trees moved to an explicit stack; the walk must keep:
// - the operands of a node evaluated before the node, the left one first;
// - the result buffer of a node leased while its operands are alive, and the right operand returned before the left;
// - only the root writing into the caller's target, through a temporary when the target overlaps an operand.
// Every tree is also checked against a BigInteger oracle, so that a snapshot of a wrong evaluation cannot pass.

/// <summary>Evaluation order of unsigned and signed expression trees.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class ExpressionEvaluationOrderTests
{
    private const int Trees = 24;
    private const int MaxDepth = 5;

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/BigIntExpressionVisitor.cs Apply -> release
    // `first` before `second` (the release lines of the operands change order)
    // mutant: BigIntExpressionVisitor.cs Apply -> `GetResultBuffer(op.NumWords, false, ...)` (the root leases a
    // temporary instead of writing into the target)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task UnsignedEvaluation_RandomTrees_KeepsTheOrderOfLeavesBuffersAndOperations() =>
        Verify(nameof(UnsignedEvaluation_RandomTrees_KeepsTheOrderOfLeavesBuffersAndOperations), UnsignedTraces);

    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Expressions/ExpressionWalk.cs Evaluate ->
    // `GetOperand(frame.Node, frame.OperandCount - 1 - frame.Next)`: the operands right to left (the leaves are read in
    // another order)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task SignedEvaluation_RandomTrees_KeepsTheOrderOfLeavesAndBuffers() =>
        Verify(nameof(SignedEvaluation_RandomTrees_KeepsTheOrderOfLeavesAndBuffers), SignedTraces);

    private static Task Verify(string name, Func<string> traces)
    {
        string text;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            text = traces();
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }

        return Verifier.Verify(text).UseFastHashSnapshotName(name);
    }

    private static string UnsignedTraces()
    {
        var lines = new List<string>();
        for (var seed = 0; seed < Trees; seed++)
        {
            using var workspace = new GpuWorkspace(1 << 16);
            var trace = new Trace();
            var random = new Random(1513 + seed);
            var values = LeafValues(random, signed: false);
            var buffers = values.Select(v => GpuBigUInt.FromBigInteger(workspace, v, Words(v))).ToArray();
            try
            {
                var leaves = buffers.Select((b, i) => new TracedUIntLeaf(b, "L" + i, trace)).ToArray();
                var (expr, expected) = UnsignedTree(random, leaves, values, MaxDepth);
                var mode = seed % 3;
                using var wide = new GpuBigUInt(workspace, expr.Node.NumWords + 2);
                // An operand's own buffer as the target, when one is wide enough: the root computes into a temporary
                var overlapping = buffers.FirstOrDefault(b => b.NumWords >= expr.Node.NumWords);
                var target = mode switch
                {
                    1 => wide,
                    2 when !overlapping.Handle.IsDefault => overlapping,
                    _ => (GpuBigUInt?)null
                };

                lines.Add($"tree {seed} words={expr.Node.NumWords} target={(target is null ? "none" : mode == 1 ? "wide" : "operand")}");
                Manager(workspace).Observer = trace;
                var recorder = new RecordingArithmetic(trace);
                var context = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager, recorder, workspace.Stream);
                var targetMemory = target is { } t ? new ScopedBigIntMemory(t.Handle.Borrow()) : default;
                var visitor = new BigIntExpressionVisitor(context, targetMemory);
                BigInteger actual;
                using (var result = expr.Node.Accept(ref visitor))
                {
                    trace.Add("result " + trace.Handle(result.Handle));
                    using var view = new GpuBigUInt(workspace, result.Handle.Borrow());
                    actual = view.ToBigInteger();
                }

                Manager(workspace).Observer = null;
                Assert.Equal(expected, actual);
                lines.AddRange(trace.Lines);
            }
            finally
            {
                foreach (var buffer in buffers)
                {
                    buffer.Dispose();
                }
            }
        }

        return string.Join("\n", lines);
    }

    private static string SignedTraces()
    {
        var lines = new List<string>();
        for (var seed = 0; seed < Trees; seed++)
        {
            using var workspace = new GpuWorkspace(1 << 16);
            var trace = new Trace();
            var random = new Random(1423 + seed);
            var values = LeafValues(random, signed: true);
            var buffers = values
                .Select(v => new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, BigInteger.Abs(v), Words(v)), v.Sign < 0))
                .ToArray();
            try
            {
                var leaves = buffers.Select((b, i) => new TracedIntLeaf(b, "L" + i, trace)).ToArray();
                var (expr, expected) = SignedTree(random, leaves, values, MaxDepth);
                var mode = seed % 3;
                using var wide = new GpuBigUInt(workspace, expr.Node.NumWords + 2);
                var overlapping = buffers.FirstOrDefault(b => b.Value.NumWords >= expr.Node.NumWords);
                var target = mode switch
                {
                    1 => wide,
                    2 when !overlapping.Value.Handle.IsDefault => overlapping.Value,
                    _ => (GpuBigUInt?)null
                };

                lines.Add($"tree {seed} words={expr.Node.NumWords} target={(target is null ? "none" : mode == 1 ? "wide" : "operand")}");
                Manager(workspace).Observer = trace;
                BigInteger actual;
                var result = target is { } t ? expr.Evaluate(t) : expr.Evaluate(workspace);
                try
                {
                    trace.Add("result " + trace.Handle(result.Value.Handle));
                    var magnitude = result.Value.ToBigInteger();
                    actual = result.IsNegative ? -magnitude : magnitude;
                }
                finally
                {
                    if (target is null)
                    {
                        result.Dispose();
                    }
                }

                Manager(workspace).Observer = null;
                Assert.Equal(expected, actual);
                lines.AddRange(trace.Lines);
            }
            finally
            {
                foreach (var buffer in buffers)
                {
                    buffer.Dispose();
                }
            }
        }

        return string.Join("\n", lines);
    }

    private static DefaultGpuMemoryManager Manager(GpuWorkspace workspace) =>
        (DefaultGpuMemoryManager)workspace.MemoryManager;

    // Five leaves of 1 to 3 words and one of 40 words, which can serve as an overlapping target; none is zero
    private static BigInteger[] LeafValues(Random random, bool signed)
    {
        var values = new BigInteger[6];
        for (var i = 0; i < values.Length; i++)
        {
            var bits = i == values.Length - 1 ? 40 * 32 : random.Next(1, 96);
            var bytes = new byte[(bits + 7) / 8 + 1];
            random.NextBytes(bytes);
            bytes[^1] = 0;
            var value = (new BigInteger(bytes) & ((BigInteger.One << bits) - 1)) | (BigInteger.One << (bits - 1));
            values[i] = signed && random.Next(2) == 0 ? -value : value;
        }

        return values;
    }

    private static int Words(BigInteger value) => Math.Max(1, (int)((BigInteger.Abs(value).GetBitLength() + 31) / 32));

    // A random tree over the leaves; every operation is defined (no negative difference, no zero divisor)
    private static (GpuBigUIntExpr Expr, BigInteger Value) UnsignedTree(Random random, TracedUIntLeaf[] leaves,
        BigInteger[] values, int depth)
    {
        if (depth == 0 || random.Next(5) == 0)
        {
            var i = random.Next(leaves.Length);
            return (new GpuBigUIntExpr(leaves[i]), values[i]);
        }

        var (a, x) = UnsignedTree(random, leaves, values, depth - 1);
        var kind = random.Next(13);
        if (kind >= 8)
        {
            return kind switch
            {
                8 => (~a, (BigInteger.One << (32 * a.Node.NumWords)) - 1 - x),
                9 => (a << 13, x << 13),
                10 => (a >> 9, x >> 9),
                11 => (GpuBigUIntExpr.Sqrt(a), IntegerSqrt(x)),
                _ => random.Next(2) == 0
                    ? (a.Truncate(1), x & uint.MaxValue)
                    : (a.Fit(a.Node.NumWords), x)
            };
        }

        var (b, y) = UnsignedTree(random, leaves, values, depth - 1);
        switch (kind)
        {
            case 0: return (a + b, x + y);
            case 1: return x >= y ? (a - b, x - y) : (b - a, y - x);
            case 2: return (a * b, x * y);
            case 3: return y.IsZero ? (a + b, x + y) : (a / b, x / y);
            case 4: return y.IsZero ? (a + b, x + y) : (a % b, x % y);
            case 5: return (a & b, x & y);
            case 6: return (a | b, x | y);
            default: return (a ^ b, x ^ y);
        }
    }

    private static (GpuBigIntExpr Expr, BigInteger Value) SignedTree(Random random, TracedIntLeaf[] leaves,
        BigInteger[] values, int depth)
    {
        if (depth == 0 || random.Next(5) == 0)
        {
            var i = random.Next(leaves.Length);
            return (new GpuBigIntExpr(leaves[i]), values[i]);
        }

        var (a, x) = SignedTree(random, leaves, values, depth - 1);
        var kind = random.Next(10);
        if (kind >= 7)
        {
            return kind switch
            {
                7 => (-a, -x),
                8 => (~a, -x - 1),
                _ => (a.Fit(a.Node.NumWords), x)
            };
        }

        var (b, y) = SignedTree(random, leaves, values, depth - 1);
        return kind switch
        {
            0 => (a + b, x + y),
            1 => (a - b, x - y),
            2 => (a * b, x * y),
            3 => y.IsZero ? (a + b, x + y) : (a / b, BigInteger.Divide(x, y)),
            4 => y.IsZero ? (a + b, x + y) : (a % b, BigInteger.Remainder(x, y)),
            5 => (GpuBigIntExpr.Max(a, b), BigInteger.Max(x, y)),
            _ => (GpuBigIntExpr.Min(a, b), BigInteger.Min(x, y))
        };
    }

    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n < 2)
        {
            return n;
        }

        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x)
            {
                return x;
            }

            x = y;
        }
    }

    /// <summary>The recorded events of one evaluation; buffers are named by their first appearance.</summary>
    private sealed class Trace : IGpuMemoryObserver
    {
        private readonly Dictionary<IntPtr, string> _names = [];

        public List<string> Lines { get; } = [];

        public void Add(string line) => Lines.Add(line);

        public void Leased(IntPtr pointer, int numWords) => Add($"lease {Name(pointer)} {numWords}w");

        public void Released(IntPtr pointer, int numWords) => Add($"release {Name(pointer)}");

        public string Handle(GpuBigIntMemoryHandle handle) =>
            handle.IsDefault ? "none" : $"{Name(handle.Buffer.Pointer)}+{handle.WordOffset}:{handle.NumWords}";

        private string Name(IntPtr pointer)
        {
            if (!_names.TryGetValue(pointer, out var name))
            {
                name = "b" + _names.Count;
                _names.Add(pointer, name);
            }

            return name;
        }
    }

    /// <summary>An unsigned leaf implemented outside GpuMath that records when it is read.</summary>
    private sealed class TracedUIntLeaf(GpuBigUInt value, string name, Trace trace) : IGpuBigUIntExpression
    {
        public int NumWords => value.NumWords;

        public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor)
        {
            trace.Add("leaf " + name);
            return visitor.VisitGpuUIntBigInteger(value);
        }
    }

    /// <summary>A signed leaf implemented outside GpuMath that records when it is read.</summary>
    private sealed class TracedIntLeaf(GpuBigInt value, string name, Trace trace) : IGpuBigIntExpression
    {
        public int NumWords => value.Value.NumWords;

        public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
        {
            trace.Add("leaf " + name);
            return new GpuBigIntLeafExpression(value).Evaluate(workspace, targetResult);
        }
    }

    /// <summary>Records every operation with its operand and result buffers, then runs it.</summary>
    private sealed class RecordingArithmetic(Trace trace) : IGpuBigIntArithmetic
    {
        private readonly LegacyProviderArithmeticAdapter _inner;

        public void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record("add", left, right, result);
            _inner.Add(ref context, left, right, result);
        }

        public void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record("sub", left, right, result);
            _inner.Sub(ref context, left, right, result);
        }

        public void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record("mul", left, right, result);
            _inner.Mul(ref context, left, right, result);
        }

        public void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record("div", left, right, result);
            _inner.Div(ref context, left, right, result);
        }

        public void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record("mod", left, right, result);
            _inner.Mod(ref context, left, right, result);
        }

        public void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left,
            GpuBigIntMemoryHandle right, GpuBigIntMemoryHandle result)
        {
            Record("and", left, right, result);
            _inner.BitwiseAnd(ref context, left, right, result);
        }

        public void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left,
            GpuBigIntMemoryHandle right, GpuBigIntMemoryHandle result)
        {
            Record("or", left, right, result);
            _inner.BitwiseOr(ref context, left, right, result);
        }

        public void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left,
            GpuBigIntMemoryHandle right, GpuBigIntMemoryHandle result)
        {
            Record("xor", left, right, result);
            _inner.BitwiseXor(ref context, left, right, result);
        }

        public void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target,
            GpuBigIntMemoryHandle result)
        {
            Record("not", target, default, result);
            _inner.BitwiseNot(ref context, target, result);
        }

        public void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
            GpuBigIntMemoryHandle result)
        {
            Record("shl", target, default, result);
            _inner.ShiftLeft(ref context, target, shiftBits, result);
        }

        public void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
            GpuBigIntMemoryHandle result)
        {
            Record("shr", target, default, result);
            _inner.ShiftRight(ref context, target, shiftBits, result);
        }

        public void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target,
            GpuBigIntMemoryHandle result)
        {
            Record("sqrt", target, default, result);
            _inner.Sqrt(ref context, target, result);
        }

        private void Record(string operation, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result) =>
            trace.Add($"{operation} {trace.Handle(left)} {trace.Handle(right)} -> {trace.Handle(result)}");
    }
}
