#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// An expression evaluated into one of its own operands (<c>x.Value = x / y</c>, #636, design gpumath-m1-misc 4.2.4 and
/// 4.2.5): the root computes into a temporary when the target overlaps one of its operands and copies the result into
/// the target, so no kernel receives an output that overlaps an input (its pointers are <c>__restrict__</c>).
/// </summary>
/// <remarks>
/// The values are compared with <see cref="BigInteger"/>; a correct value alone does not prove the guard, because an
/// in-place kernel can happen to give the right answer. <see cref="RecordingArithmetic"/> therefore records every call
/// whose result overlaps an operand (P4).
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class ExpressionAliasingTests : IDisposable
{
    private const int Words = 64;
    private const int WideWords = 4096;
    private const int Seed = 636;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly BigInteger _a;
    private readonly BigInteger _b;

    public ExpressionAliasingTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var random = new Random(Seed);
        _a = RandomValue(random, 32 * 32);
        _b = RandomValue(random, 16 * 32) | 1;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public enum Op
    {
        Add,
        Sub,
        Mul,
        Div,
        Mod,
        And,
        Or,
        Xor
    }

    public static TheoryData<Op, bool> BinaryCases()
    {
        var data = new TheoryData<Op, bool>();
        foreach (var op in Enum.GetValues<Op>())
        {
            data.Add(op, false);
            data.Add(op, true);
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(BinaryCases))]
    public void Binary_TargetIsAnOperand_GivesTheSeparateBufferResult(Op op, bool intoRight)
    {
        // P1: the target holds a op b of the original values; P2: the other operand keeps its value; the temporary the
        // root used is returned (LiveBuffers comes back).
        // mutant: GpuBigUIntExpressionExtensions.Evaluate without using on the root result (LiveBuffers stays one up)
        using var a = GpuBigUInt.FromBigInteger(_workspace, _a, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, _b, Words);
        GpuBigUIntExpr ea = a;
        GpuBigUIntExpr eb = b;
        var before = _workspace.GetMemoryStatistics();

        Binary(op, ea, eb).Node.Evaluate(_workspace, intoRight ? b : a);

        var after = _workspace.GetMemoryStatistics();
        var expected = Expected(op, _a, _b, Words);
        Assert.True(expected == (intoRight ? b : a).ToBigInteger(), $"{op} into {(intoRight ? "b" : "a")}, seed {Seed}");
        Assert.Equal(intoRight ? _a : _b, (intoRight ? a : b).ToBigInteger());
        Assert.Equal(before.LiveBuffers, after.LiveBuffers);
    }

    public enum Unary
    {
        Not,
        ShiftLeft,
        ShiftRight,
        Sqrt
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Unary.Not)]
    [InlineData(Unary.ShiftLeft)]
    [InlineData(Unary.ShiftRight)]
    [InlineData(Unary.Sqrt)]
    public void UnaryAndShift_TargetIsTheOperand_GivesTheSeparateBufferResult(Unary op)
    {
        // 4096 words, so that a shift runs in several blocks (thread i reads the word that thread i - 1 writes)
        var value = RandomValue(new Random(Seed), WideWords * 32 - 7);
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, WideWords);
        GpuBigUIntExpr ea = a;

        UnaryExpression(op, ea).Node.Evaluate(_workspace, a);

        var modulus = BigInteger.One << (WideWords * 32);
        var expected = op switch
        {
            Unary.Not => (modulus - 1) ^ value,
            Unary.ShiftLeft => (value << 37) % modulus,
            Unary.ShiftRight => value >> 37,
            _ => BigFloatReference.IntegerSqrt(value)
        };
        Assert.True(expected == a.ToBigInteger(), $"{op}, seed {Seed}");
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Root_NeverPassesAnOverlappingResult()
    {
        // P4: with the target an operand of the root, no arithmetic call receives a result that overlaps an operand.
        // The calls are counted too, so the check is reached.
        // mutant: BigIntExpressionVisitor.GetResultBuffer without the overlap check (the root writes into its operand)
        using var a = GpuBigUInt.FromBigInteger(_workspace, _a, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, _b, Words);
        using var a0 = GpuBigUInt.FromBigInteger(_workspace, _a, Words);
        using var b0 = GpuBigUInt.FromBigInteger(_workspace, _b, Words);
        GpuBigUIntExpr ea = a;
        GpuBigUIntExpr eb = b;
        var recorder = new RecordingArithmetic();

        // Each evaluation starts from the original values (the previous one overwrote an operand)
        foreach (var op in Enum.GetValues<Op>())
        {
            EvaluateRecorded(Binary(op, ea, eb), a, recorder, a, a0, b, b0);
            EvaluateRecorded(Binary(op, ea, eb), b, recorder, a, a0, b, b0);
        }

        foreach (var op in Enum.GetValues<Unary>())
        {
            EvaluateRecorded(UnaryExpression(op, ea), a, recorder, a, a0, b, b0);
        }

        // A nested expression: only the root writes into the target
        EvaluateRecorded((ea + eb) * (ea - eb), a, recorder, a, a0, b, b0);

        Assert.Equal(8 * 2 + 4 + 3, recorder.Calls);
        Assert.Equal(0, recorder.OverlappingCalls);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ValueSetter_InPlaceUpdate_IsCorrect()
    {
        // The form users write: a.Value = a / b (the #636 repro) and a.Value = a << 5
        using var a = GpuBigUInt.FromBigInteger(_workspace, _a, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, _b, Words);

        a.Value = a.Value / b.Value;
        var quotient = a.ToBigInteger();
        a.Value = a.Value << 5;

        Assert.Equal(_a / _b, quotient);
        Assert.Equal((_a / _b) << 5, a.ToBigInteger());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LeafRoot_IntoItself_KeepsTheValue()
    {
        // d.Value = d: the root is the leaf and the target is the same memory; nothing is copied
        using var d = GpuBigUInt.FromBigInteger(_workspace, _a, Words);
        var before = _workspace.GetMemoryStatistics();

        d.Value = d.Value;

        Assert.Equal(_a, d.ToBigInteger());
        Assert.Equal(before.DriverAllocations, _workspace.GetMemoryStatistics().DriverAllocations);
    }

    private void EvaluateRecorded(GpuBigUIntExpr expression, GpuBigUInt target, RecordingArithmetic recorder,
        GpuBigUInt a, GpuBigUInt a0, GpuBigUInt b, GpuBigUInt b0)
    {
        a.CopyFrom(a0);
        b.CopyFrom(b0);
        var context = new GpuBigIntExecutionContext(_workspace, _workspace.MemoryManager, recorder, _workspace.Stream);
        var visitor = new BigIntExpressionVisitor(context, new ScopedBigIntMemory(target.Handle.Borrow()));
        using var result = expression.Node.Accept(ref visitor);
    }

    private static GpuBigUIntExpr Binary(Op op, GpuBigUIntExpr a, GpuBigUIntExpr b) =>
        op switch
        {
            Op.Add => a + b,
            Op.Sub => a - b,
            Op.Mul => a * b,
            Op.Div => a / b,
            Op.Mod => a % b,
            Op.And => a & b,
            Op.Or => a | b,
            _ => a ^ b
        };

    private static GpuBigUIntExpr UnaryExpression(Unary op, GpuBigUIntExpr a) =>
        op switch
        {
            Unary.Not => ~a,
            Unary.ShiftLeft => a << 37,
            Unary.ShiftRight => a >> 37,
            _ => GpuBigUIntExpr.Sqrt(a)
        };

    private static BigInteger Expected(Op op, BigInteger a, BigInteger b, int words)
    {
        var modulus = BigInteger.One << (words * 32);
        return op switch
        {
            Op.Add => (a + b) % modulus,
            Op.Sub => a - b,
            Op.Mul => a * b % modulus,
            Op.Div => a / b,
            Op.Mod => a % b,
            Op.And => a & b,
            Op.Or => a | b,
            _ => a ^ b
        };
    }

    // A value of exactly 'bits' bits
    private static BigInteger RandomValue(Random random, int bits)
    {
        var bytes = new byte[(bits + 7) / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var value = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
        return value | (BigInteger.One << (bits - 1));
    }

    /// <summary>
    /// Delegates to the arithmetic the expressions use and counts the calls whose result overlaps an operand.
    /// </summary>
    private sealed class RecordingArithmetic : IGpuBigIntArithmetic
    {
        private readonly LegacyProviderArithmeticAdapter _inner;

        public int Calls { get; private set; }

        public int OverlappingCalls { get; private set; }

        public void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.Add(ref context, left, right, result);
        }

        public void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.Sub(ref context, left, right, result);
        }

        public void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.Mul(ref context, left, right, result);
        }

        public void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.Div(ref context, left, right, result);
        }

        public void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
            GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.Mod(ref context, left, right, result);
        }

        public void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left,
            GpuBigIntMemoryHandle right, GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.BitwiseAnd(ref context, left, right, result);
        }

        public void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left,
            GpuBigIntMemoryHandle right, GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.BitwiseOr(ref context, left, right, result);
        }

        public void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left,
            GpuBigIntMemoryHandle right, GpuBigIntMemoryHandle result)
        {
            Record(result, left, right);
            _inner.BitwiseXor(ref context, left, right, result);
        }

        public void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target,
            GpuBigIntMemoryHandle result)
        {
            Record(result, target, default);
            _inner.BitwiseNot(ref context, target, result);
        }

        public void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
            GpuBigIntMemoryHandle result)
        {
            Record(result, target, default);
            _inner.ShiftLeft(ref context, target, shiftBits, result);
        }

        public void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
            GpuBigIntMemoryHandle result)
        {
            Record(result, target, default);
            _inner.ShiftRight(ref context, target, shiftBits, result);
        }

        public void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target,
            GpuBigIntMemoryHandle result)
        {
            Record(result, target, default);
            _inner.Sqrt(ref context, target, result);
        }

        private void Record(GpuBigIntMemoryHandle result, GpuBigIntMemoryHandle operand0,
            GpuBigIntMemoryHandle operand1)
        {
            Calls++;
            if (DeviceRanges.Overlaps(result, operand0) || DeviceRanges.Overlaps(result, operand1))
            {
                OverlappingCalls++;
            }
        }
    }
}
