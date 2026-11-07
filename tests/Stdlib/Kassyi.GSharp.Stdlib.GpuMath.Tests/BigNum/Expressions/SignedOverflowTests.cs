#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The overflow check of the signed <see cref="GpuBigIntExpr"/> operations whose magnitudes are added: the result keeps
/// the width of the operands, the GPU flags a carry out of the top word without waiting, and the next synchronization of
/// the workspace stream throws <see cref="GpuOverflowException"/> naming the operation and the evaluation call.
/// </summary>
/// <remarks>
/// Every test uses its own workspace, so the overflow count starts at 0. The line of an evaluation is taken on the same
/// line as the call (<see cref="Line"/>), so the expected origin moves with the code.
/// </remarks>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class SignedOverflowTests : IDisposable
{
    private const uint Max = uint.MaxValue;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly List<GpuBigInt> _values = [];

    public SignedOverflowTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        foreach (var value in _values)
        {
            value.Dispose();
        }

        _workspace.Dispose();
        _scope.Dispose();
    }

    /// <summary>The magnitudes are added: <c>(+a) + (+b)</c>, <c>(-a) - (+b)</c>, <c>(-a) + (-b)</c>, <c>(+a) - (-b)</c>.</summary>
    public static TheoryData<string, long, long> CarryOutOfTheTopWord() => new()
    {
        { "+", Max, 1 },
        { "-", -Max, 1 },
        { "+", -Max, -1 },
        { "-", Max, -1 },
    };

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(CarryOutOfTheTopWord))]
    public void MagnitudesAdded_CarryOut_ThrowsAtTheNextRead(string op, long a, long b)
    {
        // 0xFFFFFFFF + 1 in 1 word gave 0 (and -0xFFFFFFFF - 1 gave -0) with no error. Evaluate itself does not wait for
        // the GPU, so it returns; the read of the value throws, naming the operator and the Evaluate line.
        // mutant: SignedMagnitude.AddChecked without status.MarkPending() (the read does not copy the status words)
        // mutant: CheckedParallelAddKernel without ReportOverflow (the count stays 0)
        var expression = Apply(op, Create(a, 1), Create(b, 1));

        var (result, line) = (expression.Evaluate(_workspace), Line());
        using (result)
        {
            var exception = Assert.Throws<GpuOverflowException>(() => result.Value.ToBigInteger());

            Assert.Contains($"`{op}` result does not fit in 1 word(s)", exception.Message);
            Assert.Contains($"{nameof(SignedOverflowTests)}.cs:line {line}", exception.Message);
            var origin = Assert.Single(exception.Origins);
            Assert.Equal(
                (op, 1, $"x0 {op} x1", $"[x0 {op} x1]", "Evaluate", nameof(MagnitudesAdded_CarryOut_ThrowsAtTheNextRead), line),
                (origin.Operation, origin.ResultWords, origin.Subexpression, origin.Expression, origin.EntryPoint,
                    origin.CallerMemberName, origin.CallerLineNumber));
            Assert.EndsWith($"{nameof(SignedOverflowTests)}.cs", origin.CallerFilePath);
        }
    }

    /// <summary>The word count, the operands and whether the exact sum needs another word.</summary>
    public static TheoryData<int, BigInteger, BigInteger, bool> TopWordBoundaries() => new()
    {
        { 1, Max - 1, 1, false },
        { 1, Max, 0, false },
        { 1, Max, Max, true },
        { 2, Max, 1, false },
        { 33, AllOnes(33), 1, true },
        { 33, AllOnes(32), 1, false },
        { 64, AllOnes(64), 1, true },
        { 64, AllOnes(63), 1, false },
        { 1025, AllOnes(1025), 1, true },
        { 1025, AllOnes(1024), 1, false },
        { 1025, BigInteger.One << (32 * 1024), BigInteger.One << (32 * 1024), false },
        { 1025, BigInteger.One << ((32 * 1025) - 1), BigInteger.One << ((32 * 1025) - 1), true },
    };

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(TopWordBoundaries))]
    public void Sum_ExactWhenItFits_ThrowsWhenItNeedsAnotherWord(int words, BigInteger a, BigInteger b, bool overflows)
    {
        // The carry is taken from the top word, also when it lies in the second chunk of the 1024-thread block (1025
        // words) and when the block has threads past the top word (33 words). A carry that stays inside the width is exact.
        // mutant: CheckedParallelAddKernel `if (carry != 0)` -> `if (carry == 0)` (every row flips)
        // mutant: DeviceAddSubAlignedParallelWithCarry `idx == numWords - 1` -> `idx == numWords - 2` (the 1-word rows and
        //         the top-bit rows stop overflowing)
        using var result = ((GpuBigIntExpr)Create(a, words) + Create(b, words)).Evaluate(_workspace);

        if (overflows)
        {
            Assert.Throws<GpuOverflowException>(() => result.Value.ToBigInteger());
        }
        else
        {
            Assert.Equal(a + b, result.Value.ToBigInteger());
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Report_MarksTheOverflowingNodeInTheWholeExpression()
    {
        // Only the inner sum overflows; the outer sum adds 0 and fits. The operands are named in the order they appear.
        // mutant: SignedExpressionText marks every node (the expression reads [[x0 + x1] + x2])
        var expression = ((GpuBigIntExpr)Create(Max, 1) + Create(1, 1)) + Create(0, 1);

        using var result = expression.Evaluate(_workspace);
        var exception = Assert.Throws<GpuOverflowException>(() => result.Value.ToBigInteger());

        var origin = Assert.Single(exception.Origins);
        Assert.Equal(("+", "x0 + x1", "[x0 + x1] + x2"), (origin.Operation, origin.Subexpression, origin.Expression));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Report_ParenthesizesInnerOperations()
    {
        // The outer sum overflows; its left operand is an inner operation.
        // mutant: SignedExpressionText does not parenthesize an inner operation (the expression reads [x0 + x1 + x2])
        var expression = ((GpuBigIntExpr)Create(Max - 1, 1) + Create(1, 1)) + Create(1, 1);

        using var result = expression.Evaluate(_workspace);
        var exception = Assert.Throws<GpuOverflowException>(() => result.Value.ToBigInteger());

        var origin = Assert.Single(exception.Origins);
        Assert.Equal(("(x0 + x1) + x2", "[(x0 + x1) + x2]"), (origin.Subexpression, origin.Expression));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public async Task CompiledGraph_OverflowOfAReplay_NamesCompileToGraph()
    {
        // The graph replays the origin id recorded in the capture, so an overflow of a later launch names the
        // CompileToGraph line; a launch that fits after it does not throw again, and ExecuteAsync faults like Execute.
        // mutant: GraphHandles without Origins.Pin (the ring of a later evaluation loses the origin; see the loop below)
        // mutant: GpuDeviceStatus.ClaimOverflows reports the full count instead of the new overflows (the fitting
        //         launch throws)
        var x = Create(1, 1);
        var expression = (GpuBigIntExpr)x + Create(2, 1);
        var (graph, line) = (expression.CompileToGraph(_workspace), Line());
        using (graph)
        {
            graph.Execute();
            Assert.Equal(3, graph.Result.Value.ToBigInteger());

            // Enough direct evaluations to wrap the host ring of origin ids
            for (var i = 0; i <= GpuOverflowOriginRegistry.Capacity; i++)
            {
                using var unrelated = ((GpuBigIntExpr)Create(1, 1) + Create(1, 1)).Evaluate(_workspace);
            }

            Write(x, Max);
            var exception = Assert.Throws<GpuOverflowException>(graph.Execute);
            var origin = Assert.Single(exception.Origins);
            Assert.Equal(("CompileToGraph", line), (origin.EntryPoint, origin.CallerLineNumber));

            Write(x, 1);
            graph.Execute();
            Assert.Equal(3, graph.Result.Value.ToBigInteger());

            Write(x, Max);
            await Assert.ThrowsAsync<GpuOverflowException>(async () => await graph.ExecuteAsync());
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SeveralOverflows_OneReportListsThemInOrder_AndTheNextReadIsClean()
    {
        // Two overflows before a read are reported together, oldest first; the count only grows, and the read after
        // the report finds nothing new.
        // mutant: GpuDeviceStatus.ClaimOverflows iterates the ring newest first (the origins come in reverse)
        var (first, firstLine) = (((GpuBigIntExpr)Create(Max, 1) + Create(1, 1)).Evaluate(_workspace), Line());
        var (second, secondLine) = (((GpuBigIntExpr)Create(Max, 1) - Create(-1, 1)).Evaluate(_workspace), Line());
        using (first)
        using (second)
        {
            var exception = Assert.Throws<GpuOverflowException>(() => second.Value.ToBigInteger());

            Assert.Equal(
                new (string?, int)[] { ("+", firstLine), ("-", secondLine) },
                exception.Origins.Select(static o => (o.Operation, o.CallerLineNumber)).ToArray());
            Assert.Contains("1 more overflow(s)", exception.Message);
            Assert.Equal(0, exception.OverwrittenOrigins);
            Assert.Equal(BigInteger.Zero, first.Value.ToBigInteger());
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void MoreOverflowsThanTheDeviceRing_TheEarliestAreCountedAsOverwritten()
    {
        // The device keeps the origins of the latest 8 overflows; the 2 before them are counted.
        // mutant: GpuDeviceStatus.ClaimOverflows `Math.Min(total, OverflowRingEntries)` -> `total` (10 origins, two of
        //         them read from overwritten entries)
        for (var i = 0; i < 10; i++)
        {
            using var result = ((GpuBigIntExpr)Create(Max, 1) + Create(1, 1)).Evaluate(_workspace);
        }

        var exception = Assert.Throws<GpuOverflowException>(() => _workspace.Provider.SynchronizeStream(_workspace.Stream));

        Assert.Equal(8, exception.Origins.Count);
        Assert.All(exception.Origins, static o => Assert.True(o.IsKnown));
        Assert.Equal(2, exception.OverwrittenOrigins);
        Assert.Contains("2 earlier overflow(s) were counted", exception.Message);
        _workspace.Provider.SynchronizeStream(_workspace.Stream);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CompileToGraph_OverflowInTheWarmUp_FailsTheCompilation()
    {
        // The warm-up waits for the GPU before the capture, so an overflow of the expression fails the compilation
        // instead of the first launch.
        // mutant: GpuBigIntExpr.CompileInScope without the SynchronizeStream after the warm-up (it compiles)
        var expression = (GpuBigIntExpr)Create(Max, 1) + Create(1, 1);

        var exception = Assert.Throws<InvalidOperationException>(() => expression.CompileToGraph(_workspace));

        var overflow = Assert.IsType<GpuOverflowException>(exception.InnerException);
        Assert.Equal("CompileToGraph", Assert.Single(overflow.Origins).EntryPoint);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_WithAnUnreportedOverflow_DoesNotThrow()
    {
        // An overflow nobody read is dropped with the workspace: Dispose may run while another exception unwinds.
        // mutant: GpuWorkspace.Dispose synchronizes through the provider before releasing the status (it throws)
        var exception = Record.Exception(() =>
        {
            var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
            using var a = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, Max, 1), false);
            using var b = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 1), false);
            var result = ((GpuBigIntExpr)a + b).Evaluate(workspace);
            result.Dispose();
            workspace.Dispose();
        });

        Assert.Null(exception);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void OperatorStackTraceSwitch_TheExceptionPointsAtTheOperatorLine()
    {
        // With the switch set when the expression is built, the operator records its line, and the exception's stack
        // trace starts there.
        // mutant: GpuOverflowReport.CaptureOperatorStackTrace `new StackTrace(2, true)` -> `new StackTrace(1, true)`
        //         (the trace starts in the operator, not at this line)
        // mutant: GpuOverflowReport.Create without SetRemoteStackTrace
        AppContext.SetSwitch(GpuOverflowReport.CaptureOperatorStackTracesSwitch, true);
        GpuBigIntExpr expression;
        int line;
        try
        {
            (expression, line) = ((GpuBigIntExpr)Create(Max, 1) + Create(1, 1), Line());
        }
        finally
        {
            AppContext.SetSwitch(GpuOverflowReport.CaptureOperatorStackTracesSwitch, false);
        }

        using var result = expression.Evaluate(_workspace);
        var exception = Assert.Throws<GpuOverflowException>(() => result.Value.ToBigInteger());

        var origin = Assert.Single(exception.Origins);
        var trace = origin.OperatorStackTrace ?? "";
        var firstFrame = trace[..Math.Max(0, trace.IndexOf(Environment.NewLine, StringComparison.Ordinal))];
        Assert.Contains(nameof(OperatorStackTraceSwitch_TheExceptionPointsAtTheOperatorLine), firstFrame);
        Assert.Contains($":line {line}", firstFrame);
        Assert.StartsWith(firstFrame, exception.StackTrace);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void OperatorStackTraceSwitch_Off_RecordsNoStackTrace()
    {
        // The capture costs microseconds per operator, so it is off unless the switch is set.
        // mutant: GpuOverflowReport.CaptureOperatorStackTrace always captures
        using var result = ((GpuBigIntExpr)Create(Max, 1) + Create(1, 1)).Evaluate(_workspace);
        var exception = Assert.Throws<GpuOverflowException>(() => result.Value.ToBigInteger());

        Assert.Null(Assert.Single(exception.Origins).OperatorStackTrace);
    }

    private static int Line([CallerLineNumber] int line = 0) => line;

    private static BigInteger AllOnes(int words) => (BigInteger.One << (32 * words)) - 1;

    private static GpuBigIntExpr Apply(string op, GpuBigInt a, GpuBigInt b) =>
        op == "+" ? (GpuBigIntExpr)a + b : (GpuBigIntExpr)a - b;

    private GpuBigInt Create(BigInteger value, int words)
    {
        var created = new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, BigInteger.Abs(value), words), value.Sign < 0);
        _values.Add(created);
        return created;
    }

    private void Write(GpuBigInt operand, uint word) =>
        _workspace.Provider.CopyHostToDevice(operand.Value.DevicePointer, [word], 1, _workspace.Stream);
}
