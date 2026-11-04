#nullable enable
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Differential;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Property and soundness tests for <see cref="ScalarFuzzGenerator"/> and <see cref="KnownIssueFilter"/>.
/// </summary>
public class ScalarFuzzGeneratorTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FuzzGenerator_SameSeedAndSwarm_EmitsBitIdenticalSource()
    {
        for (ulong seed = 1UL; seed <= 16UL; seed++)
        {
            int size = 1 + (int)((seed - 1UL) % 6UL);
            var optionsA = new FuzzOptions
            {
                FuzzSeed = seed,
                InputSeed = (int)(seed % 5UL),
                BatchSize = 4,
                Size = size,
            };
            var optionsB = new FuzzOptions
            {
                FuzzSeed = seed,
                InputSeed = (int)(seed % 5UL),
                BatchSize = 4,
                Size = size,
            };

            var batchA = new ScalarFuzzGenerator(optionsA).GenerateBatch();
            var batchB = new ScalarFuzzGenerator(optionsB).GenerateBatch();

            // Two independent runs with identical (--fuzz-seed, --input-seed, size, Swarm weights) must produce byte-identical kernel and case sources.
            // mutant: ScalarFuzzGenerator.GenerateBatch() line 55: var masterRng = new FuzzRandom(_options.FuzzSeed ^ (ulong)GetHashCode());
            // input: seed = 1UL .. 16UL, InputSeed = (int)(seed % 5UL), BatchSize = 4, Size = 1..6
            Assert.Equal(batchA.KernelsSource, batchB.KernelsSource);
            Assert.Equal(batchA.CasesSource, batchB.CasesSource);

            var nextBatch = new ScalarFuzzGenerator(optionsA with { FuzzSeed = seed + 100UL }).GenerateBatch();
            Assert.NotEqual(batchA.KernelsSource, nextBatch.KernelsSource);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public async Task FuzzGenerator_SwarmSweep100Seeds_ProducesZeroRoslynAndGSharpDiagnostics()
    {
        const string SweepClassName = "FuzzSweep100Kernels";
        var generator = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 1UL,
            BatchSize = 1,
            KernelClassName = SweepClassName,
        });

        var methodSources = new List<string>(100);
        int nestedStatementsInControlFlow = 0;

        for (ulong seed = 0UL; seed < 100UL; seed++)
        {
            int size = 1 + (int)(seed % 8UL);
            var spec = generator.GenerateSingleKernel($"SweepKernel_{seed:D3}", seed, size);
            methodSources.Add(spec.KernelMethodSource);

            var root = CSharpSyntaxTree.ParseText($"class __C {{ {spec.KernelMethodSource} }}", FuzzTestCompilationHelper.ParseOptions).GetRoot();
            nestedStatementsInControlFlow += CountNestedStatementsInControlFlowAndAssertLoopVarSafety(root);
        }

        // Control-flow bodies (if/else, switch sections, bounded loops) must contain generated statements up to depth 2 while never mutating loop variables inside loop bodies.
        // mutant: ScalarFuzzGenerator.EmitNestedStatements() line 159: return immediately when state.ControlFlowDepth >= 0
        // input: SweepKernel_000 .. SweepKernel_099 (seed = 0UL .. 99UL)
        Assert.True(nestedStatementsInControlFlow > 0, "Expected random statements to be emitted inside control-flow constructs.");

        string combinedSource = FuzzTestCompilationHelper.RenderKernelCompilationUnit(
            SweepClassName,
            generator.DeviceFunctions,
            methodSources);

        foreach (string dialect in new[] { "Cpu", "Cuda" })
        {
            var tree = CSharpSyntaxTree.ParseText(
                combinedSource,
                FuzzTestCompilationHelper.ParseOptions,
                path: $"FuzzSweep100Kernels_{dialect}.cu.cs");
            var compilation = CSharpCompilation.Create(
                $"FuzzSweep100_{dialect}",
                [tree],
                FuzzTestCompilationHelper.BaseReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

            var configProvider = new FuzzTestCompilationHelper.FuzzTestAnalyzerConfigOptionsProvider(dialect);
            var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, configProvider);
            var analyzerDiags = await compilation
                .WithAnalyzers(FuzzTestCompilationHelper.KernelAnalyzers, analyzerOptions)
                .GetAnalyzerDiagnosticsAsync();

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: FuzzTestCompilationHelper.ParseOptions,
                optionsProvider: configProvider);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiags);

            var csErrors = new List<Diagnostic>();
            foreach (var d in outputCompilation.GetDiagnostics())
            {
                if (d.Severity == DiagnosticSeverity.Error)
                {
                    csErrors.Add(d);
                }
            }

            var gshDiags = new List<Diagnostic>();
            foreach (var d in analyzerDiags)
            {
                if (d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                    d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                {
                    gshDiags.Add(d);
                }
            }

            foreach (var d in generatorDiags)
            {
                if (d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                    d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                {
                    gshDiags.Add(d);
                }
            }

            foreach (var d in outputCompilation.GetDiagnostics())
            {
                if (d.Id.StartsWith("GSH", StringComparison.Ordinal) &&
                    d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                {
                    gshDiags.Add(d);
                }
            }

            // Generated kernels across all 100 seeds (including composite structs, control flow, and [GpuDeviceFunction] DAGs) must compile with zero C# errors and zero GSH* errors/warnings.
            // mutant: ScalarFuzzGenerator.EmitRandomStatement() line 365: state.Statements.Add($"var {newName} = 0;"); (triggers CS0029 on bool local)
            // input: seed = 0UL .. 99UL, dialect in ["Cpu", "Cuda"]
            Assert.Empty(csErrors);
            // mutant: ScalarFuzzGenerator.InitializeScalarLocals() line 219: state.Statements.Add("object v_heap = new object();"); (triggers GSH0001)
            // input: seed = 0UL .. 99UL, dialect in ["Cpu", "Cuda"]
            Assert.Empty(gshDiags);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void FuzzGenerator_DynamicGuards_NeverThrowOrRaceInSequentialAndLockstepOracles()
    {
        const int KernelCount = 50;
        const string GuardClassName = "FuzzGuardTestKernels";
        var generator = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 0UL,
            BatchSize = 1,
            KernelClassName = GuardClassName,
        });
        var specs = new List<GeneratedKernelSpec>(KernelCount);
        var methodSources = new List<string>(KernelCount);

        for (int i = 0; i < KernelCount; i++)
        {
            var spec = generator.GenerateSingleKernel($"GuardKernel_{i:D2}", (ulong)i, size: 1 + (i % 6));
            specs.Add(spec);
            methodSources.Add(spec.KernelMethodSource);
        }

        string combinedSource = FuzzTestCompilationHelper.RenderKernelCompilationUnit(
            GuardClassName,
            generator.DeviceFunctions,
            methodSources);

        var assembly = FuzzTestCompilationHelper.CompileToInMemoryAssembly("FuzzGuardTestAssembly", combinedSource);
        var kernelType = assembly.GetType($"Kassyi.GSharp.Tests.Shared.Differential.{GuardClassName}")
            ?? throw new InvalidOperationException($"Failed to resolve {GuardClassName} type.");

        var shape = LaunchShapes.PartialWarp(rows: 64, blockX: 32);
        int[] seeds = [0, 1, 2, 3, 4];

        foreach (var spec in specs)
        {
            var action = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, spec.KernelName);

            foreach (int seed in seeds)
            {
                var rng = new DiffRandom(seed);
                var (inA, inB) = DiffValues.Int32Pairs(rng, shape.Rows);
                var inC = DiffValues.Values(rng, shape.Rows, DiffValues.ShiftCounts, DiffValues.NextInt32);
                if (seed == 0 && shape.Rows >= 4)
                {
                    inA[0] = int.MinValue;
                    inB[0] = -1;
                    inA[1] = int.MinValue;
                    inB[1] = 0;
                    inB[2] = 0;
                    inB[3] = -1;
                }

                ulong[] forwardOut = new ulong[shape.Rows];
                ulong[] reverseOut = new ulong[shape.Rows];
                ulong[] permutedOut = new ulong[shape.Rows];

                var forwardException = Record.Exception(() =>
                    SequentialOracle.Run(shape, ThreadOrder.Forward, () => action(inA, inB, inC, forwardOut)));
                var reverseException = Record.Exception(() =>
                    SequentialOracle.Run(shape, ThreadOrder.Reverse, () => action(inA, inB, inC, reverseOut)));
                var permutedException = Record.Exception(() =>
                    SequentialOracle.Run(shape, ThreadOrder.Permutation(seed), () => action(inA, inB, inC, permutedOut)));

                // Every generated kernel (including stackalloc Span<T>, nested control flow, and narrow sbyte/short /= and %= compound assignments) must complete without DivideByZeroException / OverflowException / IndexOutOfRangeException and produce identical outputs across all thread orders.
                // mutant: FuzzScalarExpressionEmitter.WrapDivisorForCompoundAssignment() line 264: change `(({rawDivisor}) == (sbyte)0) ? (sbyte)3 : ({rawDivisor})` to `({rawDivisor})`
                // input: GuardKernel_00 .. GuardKernel_49 with seed = 0 (inA[0] = int.MinValue, inB[0] = -1; inA[1] = int.MinValue, inB[1] = 0)
                Assert.Null(forwardException);
                Assert.Null(reverseException);
                Assert.Null(permutedException);
                Assert.Equal(forwardOut, reverseOut);
                Assert.Equal(forwardOut, permutedOut);
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KnownIssueFilter_CatalogAndAvoidanceHandlers_HaveOneToOneMappingAndRejectInvalidJson()
    {
        var entries = KnownIssueFilter.LoadEntries();
        var catalogAvoidIssues = new HashSet<int>();
        KnownIssueEntry? msvcClassifyEntry = null;

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.AvoidedShape))
            {
                catalogAvoidIssues.Add(entry.Issue);
            }

            if (string.Equals(entry.Kind, "classify", StringComparison.OrdinalIgnoreCase) && entry.Issue == 1078)
            {
                msvcClassifyEntry = entry;
            }
        }

        // Every "avoid" entry in known-issues.json must have a code avoidance handler in KnownIssueFilter.SupportedAvoidanceIssues, and every code avoidance handler must have an entry in known-issues.json.
        // mutant: KnownIssueFilter.s_supportedAvoidanceIssues: remove `Issue1101CoalesceAssignValue` from the set
        // input: KnownIssueFilter.LoadEntries() vs KnownIssueFilter.SupportedAvoidanceIssues
        Assert.True(
            catalogAvoidIssues.SetEquals(KnownIssueFilter.SupportedAvoidanceIssues),
            $"Catalog avoid issues [{string.Join(", ", catalogAvoidIssues)}] must match SupportedAvoidanceIssues [{string.Join(", ", KnownIssueFilter.SupportedAvoidanceIssues)}].");

        Assert.NotNull(msvcClassifyEntry);
        Assert.Equal(["Native"], msvcClassifyEntry.Paths);
        Assert.NotNull(msvcClassifyEntry.Shapes);
        Assert.Equal(3, msvcClassifyEntry.Shapes.Count);
        Assert.False(string.IsNullOrWhiteSpace(msvcClassifyEntry.RemovalCondition));

        string missingPath = Path.Combine(Path.GetTempPath(), $"missing_known_issues_{Guid.NewGuid():N}.json");
        Assert.Throws<FileNotFoundException>(() => KnownIssueFilter.LoadEntries(missingPath));

        string corruptPath = Path.Combine(Path.GetTempPath(), $"corrupt_known_issues_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(corruptPath, "{ not valid json");
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => KnownIssueFilter.LoadEntries(corruptPath));
        }
        finally
        {
            if (File.Exists(corruptPath))
            {
                File.Delete(corruptPath);
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void FuzzGenerator_KnownIssueAvoidanceSwitches_ExcludeAvoidedShapesAcross100Seeds()
    {
        const int SeedCount = 100;

        var genProfileG = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 1UL,
            BatchSize = 1,
            Profile = "G",
        });

        int narrowIncDecValueAvoided = 0;
        int mixedWidthCoalesceAvoided = 0;
        int nullableLocalAvoided = 0;
        int plainStructMethodAvoided = 0;
        int coalesceAssignValueAvoided = 0;
        int complexTupleDeconstructAvoided = 0;
        int structConversionDefaultAvoided = 0;
        int msvcNegatedConstDivAvoided = 0;

        for (ulong seed = 0UL; seed < SeedCount; seed++)
        {
            var spec = genProfileG.GenerateSingleKernel($"AvoidG_{seed:D3}", seed, size: 4);
            var root = CSharpSyntaxTree.ParseText($"class __C {{ {spec.KernelMethodSource} }}", FuzzTestCompilationHelper.ParseOptions).GetRoot();

            narrowIncDecValueAvoided += CountNarrowIncDecInValueContext(root);
            mixedWidthCoalesceAvoided += CountMixedWidthNullCoalesce(root);
            nullableLocalAvoided += CountNullableLocalDeclarations(root);
            plainStructMethodAvoided += CountPlainStructInstanceMethodCalls(root);
            coalesceAssignValueAvoided += CountCoalesceAssignmentInValueContext(root);
            complexTupleDeconstructAvoided += CountComplexTupleDeconstructions(root);
            structConversionDefaultAvoided += CountStructWithConversionDefaultExpressions(root);
            msvcNegatedConstDivAvoided += CountNegatedConstantDivisions(root);
        }

        // All active known-issue switches (#1079, #998, #984, #645, #1101, #1102, #1103, #1078) must exclude their avoided AST shapes across 100 seeds.
        // mutant: KnownIssueFilter.AvoidNarrowIncDecValue: change `_avoidedIssues.Contains(Issue1079NarrowIncDecValue)` to `false`
        // input: Profile = "G", AllowedIssues = [], seed = 0UL .. 99UL
        Assert.Equal(0, narrowIncDecValueAvoided);
        Assert.Equal(0, mixedWidthCoalesceAvoided);
        Assert.Equal(0, nullableLocalAvoided);
        Assert.Equal(0, plainStructMethodAvoided);
        Assert.Equal(0, coalesceAssignValueAvoided);
        Assert.Equal(0, complexTupleDeconstructAvoided);
        Assert.Equal(0, structConversionDefaultAvoided);
        Assert.Equal(0, msvcNegatedConstDivAvoided);

        var genAllowedAll = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 1UL,
            BatchSize = 1,
            Profile = "G",
            AllowedIssues = new HashSet<int> { 1079, 998, 984, 645, 1101, 1102, 1103, 1078 },
        });

        int narrowIncDecValueAllowed = 0;
        int mixedWidthCoalesceAllowed = 0;
        int nullableLocalAllowed = 0;
        int plainStructMethodAllowed = 0;
        int coalesceAssignValueAllowed = 0;
        int complexTupleDeconstructAllowed = 0;
        int structConversionDefaultAllowed = 0;
        int msvcNegatedConstDivAllowed = 0;

        for (ulong seed = 0UL; seed < 30UL; seed++)
        {
            var spec = genAllowedAll.GenerateSingleKernel($"AllowAll_{seed:D3}", seed, size: 4);
            var root = CSharpSyntaxTree.ParseText($"class __C {{ {spec.KernelMethodSource} }}", FuzzTestCompilationHelper.ParseOptions).GetRoot();

            narrowIncDecValueAllowed += CountNarrowIncDecInValueContext(root);
            mixedWidthCoalesceAllowed += CountMixedWidthNullCoalesce(root);
            nullableLocalAllowed += CountNullableLocalDeclarations(root);
            plainStructMethodAllowed += CountPlainStructInstanceMethodCalls(root);
            coalesceAssignValueAllowed += CountCoalesceAssignmentInValueContext(root);
            complexTupleDeconstructAllowed += CountComplexTupleDeconstructions(root);
            structConversionDefaultAllowed += CountStructWithConversionDefaultExpressions(root);
            msvcNegatedConstDivAllowed += CountNegatedConstantDivisions(root);
        }

        Assert.True(narrowIncDecValueAllowed > 0, "Expected --allow-issue 1079 to re-enable narrow ++/-- in value contexts.");
        Assert.True(mixedWidthCoalesceAllowed > 0, "Expected --allow-issue 998 to re-enable mixed-width Nullable<T> ??.");
        Assert.True(nullableLocalAllowed > 0, "Expected --allow-issue 984 to re-enable Nullable<T> local variable declarations.");
        Assert.True(plainStructMethodAllowed > 0, "Expected --allow-issue 645 to re-enable plain struct instance method calls.");
        Assert.True(coalesceAssignValueAllowed > 0, "Expected --allow-issue 1101 to re-enable ??= in value contexts.");
        Assert.True(complexTupleDeconstructAllowed > 0, "Expected --allow-issue 1102 to re-enable nested/conditional tuple deconstructions.");
        Assert.True(structConversionDefaultAllowed > 0, "Expected --allow-issue 1103 to re-enable default(T) on structs with conversion operators.");
        Assert.True(msvcNegatedConstDivAllowed > 0, "Expected --allow-issue 1078 to re-enable negated dividend constant division.");
    }

    private static int CountNestedStatementsInControlFlowAndAssertLoopVarSafety(SyntaxNode root)
    {
        int nestedCount = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is IfStatementSyntax ifStmt &&
                !FuzzTestCompilationHelper.RenderNode(ifStmt.Condition).Contains("gid", StringComparison.Ordinal))
            {
                if (ifStmt.Statement is BlockSyntax thenBlock && thenBlock.Statements.Count > 1)
                {
                    nestedCount += thenBlock.Statements.Count - 1;
                }
            }
            else if (node is ForStatementSyntax forStmt && forStmt.Declaration is not null)
            {
                foreach (var v in forStmt.Declaration.Variables)
                {
                    string loopVar = v.Identifier.ValueText;
                    foreach (var bodyNode in forStmt.Statement.DescendantNodes())
                    {
                        if (bodyNode is AssignmentExpressionSyntax { Left: IdentifierNameSyntax leftId })
                        {
                            Assert.NotEqual(loopVar, leftId.Identifier.ValueText);
                        }
                    }
                }
            }
        }

        return nestedCount;
    }

    private static int CountNarrowIncDecInValueContext(SyntaxNode root)
    {
        var narrowVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            if (node is VariableDeclarationSyntax decl)
            {
                string typeName = FuzzTestCompilationHelper.RenderNode(decl.Type.WithoutTrivia());
                if (typeName is "sbyte" or "byte" or "short" or "ushort" or "char")
                {
                    foreach (var v in decl.Variables)
                    {
                        narrowVars.Add(v.Identifier.ValueText);
                    }
                }
            }
        }

        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is PrefixUnaryExpressionSyntax pre &&
                (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression)) &&
                pre.Parent is not ExpressionStatementSyntax &&
                pre.Operand is IdentifierNameSyntax preId &&
                narrowVars.Contains(preId.Identifier.ValueText))
            {
                count++;
            }
            else if (node is PostfixUnaryExpressionSyntax post &&
                     (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression)) &&
                     post.Parent is not ExpressionStatementSyntax &&
                     post.Operand is IdentifierNameSyntax postId &&
                     narrowVars.Contains(postId.Identifier.ValueText))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountMixedWidthNullCoalesce(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is BinaryExpressionSyntax bin &&
                bin.IsKind(SyntaxKind.CoalesceExpression) &&
                FuzzTestCompilationHelper.RenderNode(bin.Left).Contains("OptShort0", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountNullableLocalDeclarations(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is LocalDeclarationStatementSyntax { Declaration.Type: NullableTypeSyntax })
            {
                count++;
            }
        }

        return count;
    }

    private static int CountPlainStructInstanceMethodCalls(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ma } &&
                ma.Name.Identifier.ValueText is "Combine" or "MixWith" or "Project" or "ReadSum" or "ReadWide")
            {
                count++;
            }
        }

        return count;
    }

    private static int CountCoalesceAssignmentInValueContext(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax assign &&
                assign.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                assign.Parent is not ExpressionStatementSyntax)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountComplexTupleDeconstructions(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax { Left: TupleExpressionSyntax leftTuple } assign)
            {
                bool hasNestedLeft = false;
                foreach (var arg in leftTuple.Arguments)
                {
                    if (arg.Expression is TupleExpressionSyntax)
                    {
                        hasNestedLeft = true;
                        break;
                    }
                }

                if (hasNestedLeft || assign.Right is not TupleExpressionSyntax)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountStructWithConversionDefaultExpressions(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is DefaultExpressionSyntax defExpr)
            {
                string typeName = FuzzTestCompilationHelper.RenderNode(defExpr.Type.WithoutTrivia());
                if (typeName is "FuzzWrapInt" or "FuzzStructL3")
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountNegatedConstantDivisions(SyntaxNode root)
    {
        int count = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is BinaryExpressionSyntax bin &&
                bin.IsKind(SyntaxKind.DivideExpression) &&
                UnwrapParens(bin.Left) is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } &&
                UnwrapParens(bin.Right) is LiteralExpressionSyntax)
            {
                count++;
            }
        }

        return count;
    }

    private static ExpressionSyntax UnwrapParens(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }
}
