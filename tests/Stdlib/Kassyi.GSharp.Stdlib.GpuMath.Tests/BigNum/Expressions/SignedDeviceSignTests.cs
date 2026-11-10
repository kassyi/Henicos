#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the device-side signs of the signed nodes, against a BigInteger oracle computed from the host inputs:
// - +, -, Max, Min, ~, *, -x and Fit on values whose signs depend on the values (differences that borrow or cancel,
//   operands of either sign) give the oracle's value; a difference that cancels is +0, while Max and Min return the
//   chosen operand with its sign flag (a chosen -0 stays -0, which equals 0);
// - an expression of those nodes is captured into a CUDA graph, and each replay with new operand magnitudes (whose
//   order flips the signs of the differences) gives the oracle's value, its sign included (Result after Execute and
//   ExecuteAsync);
// - nothing is read on the host between the nodes: an expression of ten value-signed nodes is captured (a host read
//   inside a capture invalidates it) and replays correctly.

/// <summary>Device-side signs of the signed expression nodes.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class SignedDeviceSignTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public SignedDeviceSignTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public static TheoryData<int> Widths() => new() { 1, 2, 33, 64 };

    // Every operator over a device sign: (a - b) has a sign that depends on the values; the nodes above it read it
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntSignedKernels.cu.cs SignedAddSubKernel
    // `borrowed ? signB : signA` -> `signA` (a borrowed difference keeps the first sign)
    [Theory]
    [MemberData(nameof(Widths))]
    [Scenario(TestScenario.Boundary)]
    public void Operators_OverValueDependentSigns_MatchTheOracle(int words)
    {
        foreach (var (x, y, z) in Triples(words))
        {
            using var a = SignedCases.Value(_workspace, x, words);
            using var b = SignedCases.Value(_workspace, y, words);
            using var c = SignedCases.Value(_workspace, z, words);
            GpuBigIntExpr ea = a, eb = b, ec = c;
            var d = ea - eb;

            Check(d, x - y, "a - b");
            Check(ea + eb, x + y, "a + b");
            Check(d * ec, (x - y) * z, "(a - b) * c");
            Check(-d, -(x - y), "-(a - b)");
            Check(~d, -(x - y) - 1, "~(a - b)");
            Check(~ea, -x - 1, "~a");
            Check(GpuBigIntExpr.Max(d, ec), BigInteger.Max(x - y, z), "Max(a - b, c)");
            Check(GpuBigIntExpr.Min(d, ec), BigInteger.Min(x - y, z), "Min(a - b, c)");
            Check(GpuBigIntExpr.Max(ea, eb), BigInteger.Max(x, y), "Max(a, b)");
            Check(GpuBigIntExpr.Min(ea, eb), BigInteger.Min(x, y), "Min(a, b)");
            Check((d + ec).Fit(words + 1), x - y + z, "(a - b + c).Fit");
            Check(d / ec, BigInteger.Divide(x - y, z), "(a - b) / c");
            Check(d % ec, BigInteger.Remainder(x - y, z), "(a - b) % c");
        }
    }

    // x + (-x) and x - x cancel: the device writes +0
    // mutant: SignedAddSubKernel `nonZero[0] == 0u ? 0u : sign` -> `sign`
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CancellingDifferences_ArePositiveZero()
    {
        using var a = SignedCases.Value(_workspace, -12345, 2);
        using var b = SignedCases.Value(_workspace, 12345, 2);
        using var sum = ((GpuBigIntExpr)a + b).Evaluate(_workspace);
        using var difference = ((GpuBigIntExpr)a - a).Evaluate(_workspace);
        Assert.False(sum.IsNegative);
        Assert.False(difference.IsNegative);
        Assert.True(sum.Value.IsZero());
    }

    // Max and Min of operands of different word counts whose low words are equal: only the wider operand's high word
    // decides (a + b of two 2-word leaves has 3 words). Negative leaves invert the choice. Max and Min return one of
    // the operands, its sign flag included: Min(-0, +1) is the -0 operand (planned on the host), and Max(-0, a - b)
    // with a < b is the -0 operand too (chosen by the kernel, whose comparison takes -0 as 0).
    // mutant: GpuBigIntSignedKernels.cu.cs SignedSelectKernel `aWords > bWords ? aWords : bWords` -> the smaller
    // (a tie of the low words takes a); and `takeA ? flagA : flagB` -> `(takeA ? negativeA : negativeB) ? 1u : 0u`
    // (the kernel turns -0 into +0)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MaxMin_DecidedByTheWiderOperandsHighWord()
    {
        var top = SignedCases.AllOnes(64);
        foreach (var sign in new[] { 1, -1 })
        {
            using var a = SignedCases.Value(_workspace, sign * top, 2);
            using var b = SignedCases.Value(_workspace, sign * top, 2);
            using var c = SignedCases.Value(_workspace, sign * (top - 1), 2);
            GpuBigIntExpr ea = a, eb = b, ec = c;
            var (x, z) = (sign * top, sign * (top - 1));

            Check(GpuBigIntExpr.Min(ea + eb, ec), BigInteger.Min(x + x, z), $"Min(a + b, c), sign {sign}");
            Check(GpuBigIntExpr.Max(ec, ea + eb), BigInteger.Max(z, x + x), $"Max(c, a + b), sign {sign}");
            Check(GpuBigIntExpr.Max(ea + eb, ec), BigInteger.Max(x + x, z), $"Max(a + b, c), sign {sign}");
        }

        using var zero = new GpuBigInt(new GpuBigUInt(_workspace, [0u, 0u]), isNegative: true);
        using var one = SignedCases.Value(_workspace, 1, 1);
        using var min = GpuBigIntExpr.Min(zero, one).Evaluate(_workspace);
        Assert.True(min.Value.IsZero());
        Assert.True(min.IsNegative);

        // a - b with a < b has a device sign, so the kernel chooses: Max(-0, -1) is the -0 operand
        using var two = SignedCases.Value(_workspace, 2, 1);
        using var max = GpuBigIntExpr.Max(zero, (GpuBigIntExpr)one - two).Evaluate(_workspace);
        Assert.True(max.Value.IsZero());
        Assert.True(max.IsNegative);
    }

    // The same high-word decision replayed by a graph: Min(a + b, c) with low words that tie
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Graph_MinDecidedByAHighWord_Replays()
    {
        var top = SignedCases.AllOnes(64);
        using var a = SignedCases.Value(_workspace, top, 2);
        using var b = SignedCases.Value(_workspace, top, 2);
        using var c = SignedCases.Value(_workspace, top - 1, 2);
        using var graph = GpuBigIntExpr.Min((GpuBigIntExpr)a + b, c).CompileToGraph(_workspace);
        foreach (var (x, y, z) in new[] { (top, top, top - 1), ((BigInteger)1, (BigInteger)2, (BigInteger)5) })
        {
            Write(a, x);
            Write(b, y);
            Write(c, z);
            graph.Execute();
            using var owned = graph.Result.ToOwned();
            Assert.Equal(BigInteger.Min(x + y, z), SignedCases.Read(owned));
        }
    }

    // Disposing the graph while ExecuteAsync waits: the task completes, the sign word is not read
    // mutant: GpuBigIntExpr.cs GraphHandles.ReadSign without the IsDestroyed return (the task faults with
    // ObjectDisposedException)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ExecuteAsync_ThenDispose_CompletesTheTask()
    {
        // Operands of 2^20 words: the one-block kernels of the graph run for milliseconds, so Dispose comes before the
        // wait ends
        const int Words = 1 << 20;
        using var a = SignedCases.Value(_workspace, 5, Words);
        using var b = SignedCases.Value(_workspace, -9, Words);
        var expression = GpuBigIntExpr.Max((GpuBigIntExpr)a + b, (GpuBigIntExpr)b - a);
        var graph = expression.CompileToGraph(_workspace);
        var task = graph.ExecuteAsync();
        graph.Dispose();
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    public static TheoryData<string> GraphShapes() =>
        new() { "a + b", "a - b", "Max(a, b)", "Min(a, b)", "~b", "(a + b) * c", "-(a + b)", "Max(a + b, c)" };

    // The graph replays with new magnitudes and reads the sign the kernels wrote; the first, second and third set of
    // magnitudes flip which operand is larger
    // mutant: GpuBigIntExpr.cs GraphHandles.ReadSign not called after Execute (the sign of the capture stays)
    [Theory]
    [MemberData(nameof(GraphShapes))]
    [Scenario(TestScenario.Boundary)]
    public async Task Graph_ReplaysNewMagnitudes_WithTheirSigns(string shape)
    {
        // a is positive, b and c negative (the leaves' signs are part of the expression)
        using var a = SignedCases.Value(_workspace, 5, 2);
        using var b = SignedCases.Value(_workspace, -9, 2);
        using var c = SignedCases.Value(_workspace, -4, 2);
        var expression = Build(shape, a, b, c);

        using var graph = expression.CompileToGraph(_workspace);
        (BigInteger A, BigInteger B, BigInteger C)[] sets = [(5, 9, 4), (9, 5, 4), (7, 7, 2)];
        for (var i = 0; i < sets.Length; i++)
        {
            var (x, y, z) = sets[i];
            Write(a, x);
            Write(b, y);
            Write(c, z);
            if (i == 1)
            {
                await graph.ExecuteAsync();
            }
            else
            {
                graph.Execute();
            }

            var expected = Oracle(shape, x, -y, -z);
            using var owned = graph.Result.ToOwned();
            Assert.True(expected == SignedCases.Read(owned),
                $"{shape} set {i}: {SignedCases.Read(owned)}, expected {expected}");
        }
    }

    // Ten value-signed nodes in one expression: a capture fails at the first host read, so a successful capture and a
    // correct replay show that the nodes read nothing on the host
    // mutant: SignedDeviceOps.AddSubtract reading the word it wrote (ReadNegative) before returning (the capture fails)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TenValueSignedNodes_AreCapturedWithoutAHostRead()
    {
        var values = new BigInteger[] { 3, -8, 5, -1, 13, -21, 2, -34, 55, -89, 144 };
        var leaves = values.Select(v => SignedCases.Value(_workspace, v, 2)).ToArray();
        try
        {
            GpuBigIntExpr e = leaves[0];
            var expected = values[0];
            for (var i = 1; i < leaves.Length; i++)
            {
                // Alternate subtraction and Max, so that every node's sign is a value's
                if (i % 2 == 1)
                {
                    e = e - leaves[i];
                    expected -= values[i];
                }
                else
                {
                    e = GpuBigIntExpr.Max(e, leaves[i]);
                    expected = BigInteger.Max(expected, values[i]);
                }
            }

            using var graph = e.CompileToGraph(_workspace);
            graph.Execute();
            using var owned = graph.Result.ToOwned();
            Assert.Equal(expected, SignedCases.Read(owned));
        }
        finally
        {
            foreach (var leaf in leaves)
            {
                leaf.Dispose();
            }
        }
    }

    private void Check(GpuBigIntExpr expression, BigInteger expected, string what)
    {
        using var result = expression.Evaluate(_workspace);
        var actual = SignedCases.Read(result);
        Assert.True(expected == actual, $"{what}: {actual}, expected {expected}");

        // Zero is +0 wherever a kernel decided the sign from the values (a product, a quotient and -x keep the XOR of
        // the signs, as on the host, and may give -0)
        Assert.True(!expected.IsZero || !result.IsNegative || what is "(a - b) / c" or "-(a - b)" or "(a - b) * c",
            $"{what}: -0");
    }

    // Magnitudes at the top of the width and just below, in every sign combination, so that differences borrow,
    // reach the top word, and cancel
    private static IEnumerable<(BigInteger, BigInteger, BigInteger)> Triples(int words)
    {
        var top = SignedCases.AllOnes(32 * words);
        var below = top - 7;
        foreach (var (x, y) in new[] { (top, below), (below, top), (top, top) })
        {
            foreach (var (sx, sy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                yield return (sx * x, sy * y, sy * (below + 3));
            }
        }
    }

    private static GpuBigIntExpr Build(string shape, GpuBigInt a, GpuBigInt b, GpuBigInt c)
    {
        GpuBigIntExpr ea = a, eb = b, ec = c;
        return shape switch
        {
            "a + b" => ea + eb,
            "a - b" => ea - eb,
            "Max(a, b)" => GpuBigIntExpr.Max(ea, eb),
            "Min(a, b)" => GpuBigIntExpr.Min(ea, eb),
            "~b" => ~eb,
            "(a + b) * c" => (ea + eb) * ec,
            "-(a + b)" => -(ea + eb),
            _ => GpuBigIntExpr.Max(ea + eb, ec)
        };
    }

    private static BigInteger Oracle(string shape, BigInteger a, BigInteger b, BigInteger c) => shape switch
    {
        "a + b" => a + b,
        "a - b" => a - b,
        "Max(a, b)" => BigInteger.Max(a, b),
        "Min(a, b)" => BigInteger.Min(a, b),
        "~b" => -b - 1,
        "(a + b) * c" => (a + b) * c,
        "-(a + b)" => -(a + b),
        _ => BigInteger.Max(a + b, c)
    };

    // A new magnitude for a leaf; its sign flag stays the one of the expression
    private void Write(GpuBigInt leaf, BigInteger magnitude) =>
        _workspace.Provider.CopyHostToDevice(leaf.Value.DevicePointer,
            SignedCases.Limbs(magnitude, leaf.Value.NumWords), leaf.Value.NumWords, _workspace.Stream);
}
