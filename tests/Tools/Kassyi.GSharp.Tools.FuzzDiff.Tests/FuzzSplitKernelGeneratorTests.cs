#nullable enable
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Tests of the split kernels of <see cref="ScalarFuzzGenerator"/> (<see cref="FuzzSplitKernelEmitter"/>): determinism,
/// the share in the default mix, valid G#, oracle twins that do not depend on the thread order, the known-issue
/// switches, and that a lowering which hoists a split point out of its statement is caught.
/// </summary>
public class FuzzSplitKernelGeneratorTests
{
    private static readonly FuzzSwarmFocus[] s_splitFocuses =
    [
        FuzzSwarmFocus.WarpShuffle,
        FuzzSwarmFocus.Barrier,
        FuzzSwarmFocus.Iterator,
        FuzzSwarmFocus.SuspendingCall,
        FuzzSwarmFocus.SplitPoints,
    ];

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void SplitFocus_SameSeed_EmitsBitIdenticalBatches()
    {
        foreach (var focus in s_splitFocuses)
        {
            for (ulong seed = 1UL; seed <= 4UL; seed++)
            {
                var options = new FuzzOptions { FuzzSeed = seed, BatchSize = 4, SwarmFocus = focus };
                var batchA = new ScalarFuzzGenerator(options).GenerateBatch();
                var batchB = new ScalarFuzzGenerator(options with { }).GenerateBatch();
                var other = new ScalarFuzzGenerator(options with { FuzzSeed = seed + 100UL }).GenerateBatch();

                // The same seed and focus must give the same kernels, twins and cases, so a finding can be regenerated.
                // mutant: FuzzSplitKernelEmitter.ChooseForms: new FuzzRandom(FuzzRandom.Mix(kernelSeed ^
                // (ulong)Environment.TickCount64))
                // input: each split focus, seed = 1 .. 4, BatchSize = 4
                Assert.Equal(batchA.KernelsSource, batchB.KernelsSource);
                Assert.Equal(batchA.CasesSource, batchB.CasesSource);
                Assert.NotEqual(batchA.KernelsSource, other.KernelsSource);
                Assert.All(batchA.Kernels, static k => Assert.NotEqual(FuzzSplitForms.None, k.SplitForms));
                Assert.Contains(FuzzSplitOracle.LaunchShapeExpression, batchA.CasesSource, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DefaultFocus_MixesFewSplitKernels_AndLeavesTheOtherKernelsUnchanged()
    {
        int splitKernels = 0;
        int ordinaryKernels = 0;
        for (ulong seed = 1UL; seed <= 4UL; seed++)
        {
            var options = new FuzzOptions { FuzzSeed = seed, BatchSize = 64 };
            var batch = new ScalarFuzzGenerator(options).GenerateBatch();
            var reference = new ScalarFuzzGenerator(options);
            foreach (var spec in batch.Kernels)
            {
                if (spec.SplitForms != FuzzSplitForms.None)
                {
                    splitKernels++;
                    string twinHeader = $"{FuzzSplitOracle.OracleName(spec.KernelName)}(";
                    Assert.Contains(twinHeader, batch.KernelsSource, StringComparison.Ordinal);
                    continue;
                }

                // A kernel that is not a split kernel must be exactly the kernel the generator made before split
                // kernels existed: the split draw uses its own random stream.
                // mutant: FuzzSplitKernelEmitter.ChooseForms: draw from new FuzzRandom(kernelSeed), the kernel's own
                // stream
                // input: default focus, seed = 1 .. 4, BatchSize = 64
                ordinaryKernels++;
                Assert.Equal(
                    reference.GenerateSingleKernel(spec.KernelName, spec.KernelSeed, spec.Size).KernelMethodSource,
                    spec.KernelMethodSource);
            }
        }

        // One kernel in 16 on average: 256 kernels give about 16; the bounds only catch a weight that is off by far.
        // mutant: FuzzSplitKernelEmitter.DefaultMixOneIn = 1
        // input: default focus, seed = 1 .. 4, BatchSize = 64
        Assert.InRange(splitKernels, 4, 40);
        Assert.Equal(256 - splitKernels, ordinaryKernels);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(FuzzSwarmFocus.WarpShuffle, "Warp.Shuffle", "Gpu.SyncThreads")]
    [InlineData(FuzzSwarmFocus.Barrier, "Gpu.SyncThreads", "Warp.Shuffle")]
    [InlineData(FuzzSwarmFocus.Iterator, "foreach (int it", "Suspend")]
    [InlineData(FuzzSwarmFocus.SuspendingCall, "Suspend", "foreach")]
    public void SplitFocus_EachMode_EmitsItsOwnShape(FuzzSwarmFocus focus, string expected, string absent)
    {
        var generator = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = 3UL, BatchSize = 1, SwarmFocus = focus });
        for (ulong seed = 0UL; seed < 24UL; seed++)
        {
            string method = generator.GenerateSingleKernel($"Focus_{seed:D2}", seed, size: 3).KernelMethodSource;

            // A focus mode must put its own kind of split point in every kernel and leave the others out, so that a run
            // with that mode is the witness for that kind.
            // mutant: FuzzSplitKernelEmitter.ChooseForms: FuzzSwarmFocus.Barrier => FuzzSplitForms.Shuffle
            // input: one focus per InlineData row, seed = 0 .. 23, size = 3
            Assert.Contains(expected, method, StringComparison.Ordinal);
            Assert.DoesNotContain(absent, method, StringComparison.Ordinal);
        }
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(FuzzSwarmFocus.WarpShuffle)]
    [InlineData(FuzzSwarmFocus.Barrier)]
    [InlineData(FuzzSwarmFocus.Iterator)]
    [InlineData(FuzzSwarmFocus.SuspendingCall)]
    [InlineData(FuzzSwarmFocus.SplitPoints)]
    public void SplitFocus_Batch_ProducesZeroRoslynAndGSharpDiagnostics(FuzzSwarmFocus focus)
    {
        var batch = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = 11UL, BatchSize = 12, SwarmFocus = focus })
            .GenerateBatch();

        // Every generated split kernel, its helpers and its oracle twins must build without a C# error or a G# error or
        // warning on both dialects: a batch with one bad kernel fails to build as a whole.
        // mutant: FuzzSplitKernelEmitter.RenderKernelMethod: emit "if (gid >= checksum.Length) { return; }" before the
        // body (barrier after a divergent return)
        // input: focus per InlineData row, fuzz seed 11, BatchSize = 12
        MetamorphicTestSupport.VerifyZeroGeneratorAndAnalyzerDiagnostics(batch.KernelsSource, "Cpu");
        MetamorphicTestSupport.VerifyZeroGeneratorAndAnalyzerDiagnostics(batch.KernelsSource, "Cuda");
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void SplitKernels_OracleTwins_NeverThrowAndAgreeAcrossThreadOrders()
    {
        var batch = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 5UL,
            BatchSize = 24,
            SwarmFocus = FuzzSwarmFocus.SplitPoints,
            AllowedIssues = new HashSet<int> { FuzzSplitKernelEmitter.SplitArgumentOrderIssue },
        }).GenerateBatch();
        var kernelType = FuzzSplitOracleTests.CompileKernelType(batch.KernelsSource, "SplitTwinOrders");
        var shape = LaunchShapes.Grid1D(2, 64);

        foreach (var spec in batch.Kernels)
        {
            var twin = FuzzTestCompilationHelper.CreateKernelDelegate(
                kernelType,
                FuzzSplitOracle.OracleName(spec.KernelName));
            for (int seed = 0; seed < 3; seed++)
            {
                var rng = new DiffRandom(seed);
                var (inA, inB) = DiffValues.Int32Pairs(rng, shape.Rows);
                var inC = DiffValues.Values(rng, shape.Rows, DiffValues.ShiftCounts, DiffValues.NextInt32);
                ulong[] forward = new ulong[shape.Rows];
                ulong[] reverse = new ulong[shape.Rows];
                ulong[] permuted = new ulong[shape.Rows];

                // A split kernel writes and reads only its own thread's values, so its twin must give the same result
                // in any thread order and never throw (a division guard or an index mask that fails would throw here).
                // mutant: FuzzSplitKernelEmitter shuffle shape 9: inB[(gid & ~3) + (S(e) & 3)] -> inB[(gid & ~3) +
                // S(e)]
                // input: SplitPoints focus with #991 allowed, fuzz seed 5, 24 kernels, input seeds 0 .. 2
                Assert.Null(Record.Exception(() => RunTwin(twin, ThreadOrder.Forward, inA, inB, inC, forward)));
                Assert.Null(Record.Exception(() => RunTwin(twin, ThreadOrder.Reverse, inA, inB, inC, reverse)));
                var permutation = ThreadOrder.Permutation(seed);
                Assert.Null(Record.Exception(() => RunTwin(twin, permutation, inA, inB, inC, permuted)));
                Assert.Equal(forward, reverse);
                Assert.Equal(forward, permuted);
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void SplitKernels_LoweringThatHoistsTheSplitOutOfItsStatement_IsCaught()
    {
        // The planted mutant is the wrong lowering of #1535: the split call moves into a temporary in front of its
        // statement, so whatever the statement evaluates left of it runs after it.
        int caughtShuffle = CountCaughtHoistMutants(FuzzSwarmFocus.WarpShuffle, out int shuffleMutants);
        int caughtSuspending = CountCaughtHoistMutants(FuzzSwarmFocus.SuspendingCall, out int suspendingMutants);
        int caughtBarrier = CountCaughtHoistMutants(FuzzSwarmFocus.Barrier, out int barrierMutants);

        // Shuffles and suspending calls placed after operands with side effects must expose the hoisting in a 16-seed
        // budget. Barrier kernels have no split inside an expression, so the mutant changes nothing there: without the
        // generated shuffles and suspending calls the hoisting would go unseen.
        // mutant: FuzzSplitKernelEmitter.SplitKernelBuilder.Shuffle: return $"({value})" (no shuffle generated; the
        // shuffle count drops to 0 of 10 caught)
        // input: WarpShuffle, SuspendingCall and Barrier focus, kernel seeds 1 .. 16, size 4, input seeds [0, 1]
        Assert.True(
            shuffleMutants > 0 && caughtShuffle > 0,
            $"shuffle: {caughtShuffle} of {shuffleMutants} hoist mutants caught");
        Assert.True(
            suspendingMutants > 0 && caughtSuspending > 0,
            $"suspending: {caughtSuspending} of {suspendingMutants} hoist mutants caught");
        Assert.Equal(0, barrierMutants);
        Assert.Equal(0, caughtBarrier);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void KnownIssueSwitches_SplitShapes_AreExcludedUntilAllowed()
    {
        var avoided = CountGatedShapes(new HashSet<int>());
        var allowed = CountGatedShapes(new HashSet<int>
        {
            FuzzSplitKernelEmitter.SplitArgumentOrderIssue,
            FuzzSplitKernelEmitter.StackArrayAcrossSplitIssue,
        });

        // While #991 and #1554 are open the generator must not emit their shapes (each would fail every run), and
        // --allow-issue must bring each shape back.
        // mutant: FuzzSplitKernelBuilder.EmitSplitStatement: avoidArgumentOrder = false
        // input: SplitPoints focus, profile C, seeds 0 .. 59, with no issue allowed and with 991 and 1554 allowed
        Assert.Equal(new GatedShapeCounts(0, 0, 0, 0), avoided);
        Assert.True(allowed.ConditionalArmSplit > 0, "split point in an arm of ?: or right of || / &&");
        Assert.True(allowed.CompoundWritesTarget > 0, "compound assignment whose split writes the target");
        Assert.True(allowed.SplitArgumentWrites > 0, "split argument that writes an earlier argument");
        Assert.True(allowed.StackArray > 0, "stack array");
    }

    private static int CountCaughtHoistMutants(FuzzSwarmFocus focus, out int mutants)
    {
        const string ClassName = "FuzzBatchKernels";
        var generator = new ScalarFuzzGenerator(new FuzzOptions { FuzzSeed = 1UL, BatchSize = 1, SwarmFocus = focus });
        var methods = new List<string>();
        var pairs = new List<(string Original, string Mutant)>();
        for (ulong seed = 1UL; seed <= 16UL; seed++)
        {
            string name = $"Hoist_{seed:D2}";
            string source = generator.GenerateSingleKernel(name, seed, size: 4).KernelMethodSource;
            methods.Add(source);
            if (HoistFirstSplit(source, $"{name}_Mut") is { } mutant)
            {
                methods.Add(mutant);
                pairs.Add((name, $"{name}_Mut"));
            }
        }

        mutants = pairs.Count;
        if (pairs.Count == 0)
        {
            return 0;
        }

        string unit = FuzzSplitOracle.WithOracleTwins(
            FuzzTestCompilationHelper.RenderKernelCompilationUnit(
                ClassName,
                generator.DeviceFunctions,
                methods,
                string.Join(Environment.NewLine + Environment.NewLine, generator.SplitHelperSources)));
        var kernelType = FuzzSplitOracleTests.CompileKernelType(unit, $"SplitHoist{focus}");

        int caught = 0;
        foreach (var (original, mutant) in pairs)
        {
            var oracle = FuzzTestCompilationHelper.CreateKernelDelegate(
                kernelType,
                FuzzSplitOracle.OracleName(original));
            var planted = FuzzTestCompilationHelper.CreateKernelDelegate(
                kernelType,
                FuzzSplitOracle.OracleName(mutant));
            var testCase = new DifferentialCase($"{ClassName}.{mutant}")
            {
                Shape = LaunchShapes.Grid1D(2, 64),
                Inputs = static (rng, shape) =>
                {
                    var (inA, inB) = DiffValues.Int32Pairs(rng, shape.Rows);
                    var inC = DiffValues.Values(rng, shape.Rows, DiffValues.ShiftCounts, DiffValues.NextInt32);
                    return new DiffBuffers()
                        .In("inA", inA)
                        .In("inB", inB)
                        .In("inC", inC)
                        .Out<ulong>("checksum", shape.Rows, ["hash"], OutputPolicy.Exact);
                },
                Launch = (b, s) => SequentialOracle.Run(
                    s,
                    ThreadOrder.Forward,
                    () => planted(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum"))),
                Oracle = (b, _) =>
                    oracle(b.Get<int>("inA"), b.Get<int>("inB"), b.Get<int>("inC"), b.Get<ulong>("checksum")),
            };
            var report = DifferentialRunner.Evaluate(
                testCase,
                seeds: [0, 1],
                paths: [new DiffPathRunner(DiffPath.Native, DiffPaths.DirectRunner)],
                known: [],
                checkStale: false);
            if (report.UnexpectedMismatches.Count > 0)
            {
                caught++;
            }
        }

        return caught;
    }

    // Moves the first split call that is part of a larger top-level assignment into a temporary declared in front of
    // the statement; returns null when the kernel has no such call.
    private static string? HoistFirstSplit(string methodSource, string mutantName)
    {
        var root = FuzzSyntaxHelpers.ParseRoot($"class __Hoist {{ {methodSource} }}");
        var method = FuzzSyntaxHelpers.GetKernelMethods(root)[0];
        if (method.Body is not { } body)
        {
            return null;
        }

        for (int i = 0; i < body.Statements.Count; i++)
        {
            if (body.Statements[i] is not ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax assignment,
                } statement)
            {
                continue;
            }

            foreach (var node in assignment.Right.DescendantNodes())
            {
                if (node is not InvocationExpressionSyntax call || !IsSplitCall(call) || call == assignment.Right)
                {
                    continue;
                }

                var temp = SyntaxFactory.IdentifierName("__hoisted");
                var rewritten = statement.ReplaceNode(call, temp);
                var declaration = SyntaxFactory.ParseStatement(
                    $"var __hoisted = {FuzzSyntaxHelpers.RenderNodeText(call)};");
                var statements = body.Statements.RemoveAt(i).InsertRange(i, [declaration, rewritten]);
                var mutant = method.WithBody(body.WithStatements(statements))
                    .WithIdentifier(SyntaxFactory.Identifier(mutantName));
                return FuzzSyntaxHelpers.RenderFullNode(mutant.NormalizeWhitespace());
            }
        }

        return null;
    }

    private static bool IsSplitCall(InvocationExpressionSyntax call) =>
        FuzzSplitOracle.IsShuffle(call, out _) ||
        FuzzSyntaxHelpers.GetInvokedMethodName(call) is { } name &&
        name.StartsWith("Suspend", StringComparison.Ordinal);

    private static void RunTwin(
        Action<int[], int[], int[], ulong[]> twin,
        ThreadOrder order,
        int[] inA,
        int[] inB,
        int[] inC,
        ulong[] checksum) =>
        SequentialOracle.Run(LaunchShapes.Grid1D(2, 64), order, () => twin(inA, inB, inC, checksum));

    private static GatedShapeCounts CountGatedShapes(HashSet<int> allowedIssues)
    {
        var generator = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 1UL,
            BatchSize = 1,
            Profile = "C",
            SwarmFocus = FuzzSwarmFocus.SplitPoints,
            AllowedIssues = allowedIssues,
        });
        var counts = new GatedShapeCounts(0, 0, 0, 0);
        for (ulong seed = 0UL; seed < 60UL; seed++)
        {
            string method = generator.GenerateSingleKernel($"Gate_{seed:D2}", seed, size: 6).KernelMethodSource;
            var root = FuzzSyntaxHelpers.ParseRoot($"class __Gate {{ {method} }}");
            counts += CountGatedShapes(root);
        }

        return counts;
    }

    private static GatedShapeCounts CountGatedShapes(SyntaxNode root)
    {
        var counts = new GatedShapeCounts(0, 0, 0, 0);
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ConditionalExpressionSyntax conditional when
                    ContainsSplit(conditional.WhenTrue) || ContainsSplit(conditional.WhenFalse):
                case BinaryExpressionSyntax binary when
                    (binary.IsKind(SyntaxKind.LogicalOrExpression) || binary.IsKind(SyntaxKind.LogicalAndExpression)) &&
                    ContainsSplit(binary.Right):
                    counts = counts with { ConditionalArmSplit = counts.ConditionalArmSplit + 1 };
                    break;
                case AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment when
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    ContainsSplit(assignment.Right) &&
                    PassesByRef(assignment.Right, target.Identifier.ValueText):
                    counts = counts with { CompoundWritesTarget = counts.CompoundWritesTarget + 1 };
                    break;
                case ArgumentSyntax argument when
                    argument.Parent?.Parent is InvocationExpressionSyntax owner &&
                    !IsSplitCall(owner) &&
                    ContainsSplitWithAssignment(argument.Expression):
                    counts = counts with { SplitArgumentWrites = counts.SplitArgumentWrites + 1 };
                    break;
                case LocalDeclarationStatementSyntax { Declaration.Type: ArrayTypeSyntax }:
                    counts = counts with { StackArray = counts.StackArray + 1 };
                    break;
            }
        }

        return counts;
    }

    private static bool ContainsSplit(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is InvocationExpressionSyntax call && IsSplitCall(call))
            {
                return true;
            }
        }

        return false;
    }

    // A split call whose own arguments write a variable.
    private static bool ContainsSplitWithAssignment(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            if (descendant is InvocationExpressionSyntax call && IsSplitCall(call) && HasAssignment(call.ArgumentList))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAssignment(SyntaxNode node)
    {
        foreach (var inner in node.DescendantNodes())
        {
            if (inner is AssignmentExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PassesByRef(SyntaxNode node, string variable)
    {
        foreach (var descendant in node.DescendantNodes())
        {
            if (descendant is ArgumentSyntax { Expression: IdentifierNameSyntax id } argument &&
                argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) &&
                id.Identifier.ValueText == variable)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct GatedShapeCounts(
        int ConditionalArmSplit,
        int CompoundWritesTarget,
        int SplitArgumentWrites,
        int StackArray)
    {
        public static GatedShapeCounts operator +(GatedShapeCounts a, GatedShapeCounts b) =>
            new(
                a.ConditionalArmSplit + b.ConditionalArmSplit,
                a.CompoundWritesTarget + b.CompoundWritesTarget,
                a.SplitArgumentWrites + b.SplitArgumentWrites,
                a.StackArray + b.StackArray);
    }
}
