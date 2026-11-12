#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>
/// Soundness tests verifying that <see cref="ScalarFuzzGenerator"/> and <see cref="FuzzChecksumEmitter"/>
/// catch all 5 planted transpiler mutants (implemented via <see cref="CSharpSyntaxRewriter"/>) within a 16-seed budget.
/// </summary>
public class FuzzPlantedMutantTests
{
    private const string MutatedDeviceFunctionSource =
        """
        [GpuDeviceFunction]
        public static int DevFunc_0_MutInNoCopy(ref int snapValNoCopy, ref FuzzStructL1 target, int delta)
        {
        int before = snapValNoCopy;
        target.A = (target.A + delta + 17);
        return (before ^ snapValNoCopy) + target.A;
        }
        """;

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void FuzzGenerator_PlantedTranspilerMutants_AreCaughtWithinFixedSeedBudget()
    {
        const int SeedBudget = 16;
        const string ProbeClassName = "FuzzMutantProbeKernels";
        var generator = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = 1UL,
            BatchSize = 1,
            KernelClassName = ProbeClassName,
        });

        var methodSources = new List<string>(SeedBudget * 6);
        for (int i = 1; i <= SeedBudget; i++)
        {
            string baseName = $"Probe_{i:D2}";
            var spec = generator.GenerateSingleKernel(baseName, (ulong)i, size: 4);
            methodSources.Add(spec.KernelMethodSource);
            methodSources.Add(ApplySyntaxMutant(spec.KernelMethodSource, $"{baseName}_MutShift", new ShiftMaskDropperRewriter()));
            methodSources.Add(ApplySyntaxMutant(spec.KernelMethodSource, $"{baseName}_MutOrder", new InterferingOperandReverserRewriter()));
            methodSources.Add(ApplySyntaxMutant(spec.KernelMethodSource, $"{baseName}_MutNarrow", new NarrowCompoundCorrupterRewriter()));
            methodSources.Add(ApplySyntaxMutant(spec.KernelMethodSource, $"{baseName}_MutTupleOrder", new TupleDeconstructionRhsReverserRewriter()));
            methodSources.Add(ApplySyntaxMutant(spec.KernelMethodSource, $"{baseName}_MutInCopy", new InParameterDefensiveCopyOmitterRewriter()));
        }

        string combinedSource = FuzzTestCompilationHelper.RenderKernelCompilationUnit(
            ProbeClassName,
            generator.DeviceFunctions,
            methodSources,
            MutatedDeviceFunctionSource);

        var assembly = FuzzTestCompilationHelper.CompileToInMemoryAssembly("FuzzMutantProbeAssembly", combinedSource);
        var kernelType = assembly.GetType($"Kassyi.GSharp.Tests.Shared.Differential.{ProbeClassName}")
            ?? throw new InvalidOperationException($"Failed to resolve {ProbeClassName}.");

        int caughtShiftMutant = 0;
        int caughtOrderMutant = 0;
        int caughtNarrowMutant = 0;
        int caughtTupleOrderMutant = 0;
        int caughtInCopyMutant = 0;

        for (int i = 1; i <= SeedBudget; i++)
        {
            string baseName = $"Probe_{i:D2}";
            var oracleDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, baseName);
            var shiftDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, $"{baseName}_MutShift");
            var orderDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, $"{baseName}_MutOrder");
            var narrowDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, $"{baseName}_MutNarrow");
            var tupleOrderDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, $"{baseName}_MutTupleOrder");
            var inCopyDel = FuzzTestCompilationHelper.CreateKernelDelegate(kernelType, $"{baseName}_MutInCopy");

            if (FuzzTestCompilationHelper.EvaluatesToMismatch(baseName, oracleDel, shiftDel))
            {
                caughtShiftMutant++;
            }

            if (FuzzTestCompilationHelper.EvaluatesToMismatch(baseName, oracleDel, orderDel))
            {
                caughtOrderMutant++;
            }

            if (FuzzTestCompilationHelper.EvaluatesToMismatch(baseName, oracleDel, narrowDel))
            {
                caughtNarrowMutant++;
            }

            if (FuzzTestCompilationHelper.EvaluatesToMismatch(baseName, oracleDel, tupleOrderDel))
            {
                caughtTupleOrderMutant++;
            }

            if (FuzzTestCompilationHelper.EvaluatesToMismatch(baseName, oracleDel, inCopyDel))
            {
                caughtInCopyMutant++;
            }
        }

        // Within the fixed 16-seed budget, the generated kernels and FNV-1a checksum must catch all five planted transpiler mutants (shift mask, operand evaluation order, narrow compound assignment, tuple deconstruction RHS order, and `in` parameter defensive copy omission).
        // mutant: FuzzChecksumEmitter.EmitChecksumFoldExpression(): $"hash = (hash ^ {siteId}UL) * {FnvPrime}UL;" (omitting value bits from FNV-1a fold)
        // input: Probe_01 .. Probe_16 (seeds 1..16, size = 4) compared against _MutShift, _MutOrder, _MutNarrow, _MutTupleOrder, and _MutInCopy across seeds [0, 1]
        Assert.True(caughtShiftMutant > 0, "Expected shift-mask mutant to be caught within 16 seeds.");
        Assert.True(caughtOrderMutant > 0, "Expected operand evaluation-order mutant to be caught within 16 seeds.");
        Assert.True(caughtNarrowMutant > 0, "Expected narrow compound-assignment mutant to be caught within 16 seeds.");
        // mutant: FuzzCompositeExpressionEmitter.EmitCoreCompositeStatements(): change `({tupleA.Name}, {tupleB.Name}) = (({tupleB.Name} += {delta}), ({tupleA.Name} ^ {tupleB.Name}));` to `({tupleA.Name}, {tupleB.Name}) = ({tupleA.Name}, {tupleB.Name});`
        // input: Probe_01 .. Probe_16 compared against _MutTupleOrder across seeds [0, 1]
        Assert.True(caughtTupleOrderMutant > 0, "Expected tuple deconstruction RHS evaluation-order mutant to be caught within 16 seeds.");
        // mutant: FuzzDeviceFunctionEmitter.EmitCoreDeviceFunctionCalls(): change `DevFunc_0(sL1.CompA, ref sL1, ...)` to `0`
        // input: Probe_01 .. Probe_16 compared against _MutInCopy across seeds [0, 1]
        Assert.True(caughtInCopyMutant > 0, "Expected in-parameter defensive-copy omission mutant to be caught within 16 seeds.");
    }

    private static string ApplySyntaxMutant(string methodSource, string mutatedName, CSharpSyntaxRewriter rewriter)
    {
        var tree = CSharpSyntaxTree.ParseText($"class __MutantContainer {{ {methodSource} }}", FuzzTestCompilationHelper.ParseOptions);
        MethodDeclarationSyntax? method = null;
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m)
            {
                method = m;
                break;
            }
        }

        if (method is null)
        {
            throw new InvalidOperationException("Failed to parse kernel method declaration for mutant.");
        }

        var renamed = method.WithIdentifier(SyntaxFactory.Identifier(mutatedName));
        var rewritten = rewriter.Visit(renamed) ?? renamed;
        return FuzzTestCompilationHelper.RenderNode(rewritten);
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }

    // Planted mutant 1: simulates C++ undefined/unmasked shift where counts >= 32 or < 0 yield 0 instead of C# `& 31` masking.
    private sealed class ShiftMaskDropperRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.Kind() is SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or SyntaxKind.UnsignedRightShiftExpression &&
                ContainsInCElementAccess(node.Right))
            {
                string lhs = FuzzTestCompilationHelper.RenderNode(node.Left.WithoutTrivia());
                string op = node.OperatorToken.ValueText;
                string rhs = FuzzTestCompilationHelper.RenderNode(node.Right.WithoutTrivia());
                return SyntaxFactory.ParseExpression($"((uint)({rhs}) >= 32U ? 0 : ({lhs} {op} {rhs}))").WithTriviaFrom(node);
            }

            return base.VisitBinaryExpression(node);
        }

        private static bool ContainsInCElementAccess(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (node is ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "inC" } })
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Planted mutant 2: reverses evaluation order of interfering binary operands right-to-left.
    private sealed class InterferingOperandReverserRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                    Left: IdentifierNameSyntax targetId,
                } assign &&
                UnwrapParentheses(assign.Right) is BinaryExpressionSyntax bin &&
                bin.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.ExclusiveOrExpression &&
                (ContainsCompoundAssignment(bin.Left) || ContainsCompoundAssignment(bin.Right)))
            {
                string target = targetId.Identifier.ValueText;
                string left = FuzzTestCompilationHelper.RenderNode(UnwrapParentheses(bin.Left));
                string op = bin.OperatorToken.ValueText;
                string right = FuzzTestCompilationHelper.RenderNode(UnwrapParentheses(bin.Right));
                return SyntaxFactory.ParseStatement($"{{ var __r = ({right}); var __l = ({left}); {target} = (__l {op} __r); }}{Environment.NewLine}")
                    .WithTriviaFrom(node);
            }

            return base.VisitExpressionStatement(node);
        }

        private static bool ContainsCompoundAssignment(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (node is AssignmentExpressionSyntax a && a.IsKind(SyntaxKind.AddAssignmentExpression))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Planted mutant 3: corrupts narrow integer compound assignment by XORing low bits on narrowing.
    private sealed class NarrowCompoundCorrupterRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is AssignmentExpressionSyntax assign &&
                !assign.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assign.Left is IdentifierNameSyntax targetId &&
                UnwrapParentheses(assign.Right) is CastExpressionSyntax { Type: PredefinedTypeSyntax preType } cast &&
                preType.Keyword.ValueText is "sbyte" or "byte" or "short" or "ushort" &&
                ContainsAddFiveAnd129(cast.Expression))
            {
                string target = targetId.Identifier.ValueText;
                string type = preType.Keyword.ValueText;
                string inner = FuzzTestCompilationHelper.RenderNode(UnwrapParentheses(cast.Expression));
                return SyntaxFactory.ParseStatement($"{target} = ({type})(({inner}) ^ 0x55);{Environment.NewLine}")
                    .WithTriviaFrom(node);
            }

            return base.VisitExpressionStatement(node);
        }

        private static bool ContainsAddFiveAnd129(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (node is LiteralExpressionSyntax { Token.ValueText: "129" })
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Planted mutant 4: reverses tuple deconstruction RHS evaluation order right-to-left.
    private sealed class TupleDeconstructionRhsReverserRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                    Left: TupleExpressionSyntax { Arguments.Count: 2 } leftTuple,
                    Right: TupleExpressionSyntax { Arguments.Count: 2 } rightTuple,
                } &&
                ContainsCompoundAssignment(rightTuple.Arguments[0].Expression))
            {
                string t1 = FuzzTestCompilationHelper.RenderNode(leftTuple.Arguments[0].Expression.WithoutTrivia());
                string t2 = FuzzTestCompilationHelper.RenderNode(leftTuple.Arguments[1].Expression.WithoutTrivia());
                string e1 = FuzzTestCompilationHelper.RenderNode(UnwrapParentheses(rightTuple.Arguments[0].Expression));
                string e2 = FuzzTestCompilationHelper.RenderNode(UnwrapParentheses(rightTuple.Arguments[1].Expression));
                return SyntaxFactory.ParseStatement($"{{ var __rhs2 = ({e2}); var __rhs1 = ({e1}); {t1} = __rhs1; {t2} = __rhs2; }}{Environment.NewLine}")
                    .WithTriviaFrom(node);
            }

            return base.VisitExpressionStatement(node);
        }

        private static bool ContainsCompoundAssignment(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodesAndSelf())
            {
                if (node is AssignmentExpressionSyntax a && a.IsKind(SyntaxKind.AddAssignmentExpression))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Planted mutant 5: omits defensive copy on `in` parameter when passing a struct member/property to DevFunc_0 alongside `ref sL1`.
    private sealed class InParameterDefensiveCopyOmitterRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (visited.Expression is IdentifierNameSyntax { Identifier.ValueText: "DevFunc_0" } &&
                visited.ArgumentList.Arguments.Count == 3 &&
                visited.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "sL1" },
                    Name.Identifier.ValueText: "CompA",
                })
            {
                var mutatedArg0 = SyntaxFactory.Argument(SyntaxFactory.ParseExpression("sL1.A"))
                    .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                var newArgs = visited.ArgumentList.Arguments.Replace(visited.ArgumentList.Arguments[0], mutatedArg0);
                return visited
                    .WithExpression(SyntaxFactory.IdentifierName("DevFunc_0_MutInNoCopy"))
                    .WithArgumentList(visited.ArgumentList.WithArguments(newArgs));
            }

            return visited;
        }
    }
}
