#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Macros;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Unit tests for <see cref="LabeledJumps"/>, <see cref="LabeledJumpSyntax"/>, <see cref="BranchTargetRewriter"/> and
/// <see cref="UnrollChecks.HasDynamicControlFlow"/> on C# 15 labeled <c>break</c> and <c>continue</c> statements (#1990).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class LabeledJumpPlanTests
{
    private static readonly (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) s_context =
        RoslynTestUtility.GetTestCaseContext("LabeledJumps/LabeledJumpPlanCases.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanJump_NamesTheInnermostLoop_IsPlain()
    {
        var method = Method("InnermostLoopNamed");
        var continueStmt = method.DescendantNodes().OfType<ContinueStatementSyntax>().Single();
        var breakStmt = method.DescendantNodes().OfType<BreakStatementSyntax>().Single();

        var continuePlan = LabeledJumps.PlanJump(continueStmt, s_context.Model);
        Assert.False(continuePlan.IsBreak);
        Assert.Equal(LabeledJumpForm.Plain, continuePlan.Form);
        Assert.Null(continuePlan.Label);

        var breakPlan = LabeledJumps.PlanJump(breakStmt, s_context.Model);
        Assert.True(breakPlan.IsBreak);
        Assert.Equal(LabeledJumpForm.Plain, breakPlan.Form);
        Assert.Null(breakPlan.Label);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlanJump_NamesAnOuterLoopFromAnInnerLoop_IsGoto()
    {
        var method = Method("NestedLoops");
        var breaks = method.DescendantNodes().OfType<BreakStatementSyntax>().ToArray();
        var continueStmt = method.DescendantNodes().OfType<ContinueStatementSyntax>().Single();

        var labeledBreakPlan = LabeledJumps.PlanJump(breaks[0], s_context.Model);
        Assert.True(labeledBreakPlan.IsBreak);
        Assert.Equal(LabeledJumpForm.Goto, labeledBreakPlan.Form);
        Assert.Equal("outer", labeledBreakPlan.Label?.Name);
        Assert.Equal(0, labeledBreakPlan.Region);

        var labeledContinuePlan = LabeledJumps.PlanJump(continueStmt, s_context.Model);
        Assert.False(labeledContinuePlan.IsBreak);
        Assert.Equal(LabeledJumpForm.Goto, labeledContinuePlan.Form);
        Assert.Equal("outer", labeledContinuePlan.Label?.Name);
        Assert.Equal(0, labeledContinuePlan.Region);

        var unlabeledBreakPlan = LabeledJumps.PlanJump(breaks[1], s_context.Model);
        Assert.True(unlabeledBreakPlan.IsBreak);
        Assert.Equal(LabeledJumpForm.Plain, unlabeledBreakPlan.Form);
        Assert.Null(unlabeledBreakPlan.Label);

        var annotated = LoweringAnnotations.AnnotateOriginalSpans(method.Body!, s_context.Compilation);
        var stamped = LoweringAnnotations.StampRegion(annotated, region: 2, s_context.Model);
        var stampedBreak = stamped.DescendantNodes().OfType<BreakStatementSyntax>().First();
        var stampedPlan = LabeledJumps.PlanJump(stampedBreak, s_context.Model);
        Assert.Equal(LabeledJumpForm.Goto, stampedPlan.Form);
        Assert.Equal("outer", stampedPlan.Label?.Name);
        Assert.Equal(2, stampedPlan.Region);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanJump_ContinueOuterInsideASwitch_IsPlain()
    {
        var method = Method("SwitchInsideLabeledLoop");
        var continueStmt = method.DescendantNodes().OfType<ContinueStatementSyntax>().Single();

        var plan = LabeledJumps.PlanJump(continueStmt, s_context.Model);
        Assert.False(plan.IsBreak);
        Assert.Equal(LabeledJumpForm.Plain, plan.Form);
        Assert.Null(plan.Label);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanJump_BreakOuterInsideASwitch_IsGoto()
    {
        var method = Method("SwitchInsideLabeledLoop");
        var breakOuter = method.DescendantNodes().OfType<BreakStatementSyntax>().First();

        var plan = LabeledJumps.PlanJump(breakOuter, s_context.Model);
        Assert.True(plan.IsBreak);
        Assert.Equal(LabeledJumpForm.Goto, plan.Form);
        Assert.Equal("outer", plan.Label?.Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindTarget_TwoLabelsOnOneLoop_OnlyTheImmediateOneCounts()
    {
        var method = Method("TwoLabelsOnOneLoop");
        var whileLoop = method.DescendantNodes().OfType<WhileStatementSyntax>().Single();
        var breakB = method.DescendantNodes().OfType<BreakStatementSyntax>().Single();

        Assert.Same(whileLoop, LabeledJumps.FindTarget(breakB));

        var breakA = LabeledJumpSyntax.WithLabelName(breakB, SyntaxFactory.IdentifierName("a"));
        var replaced = method.ReplaceNode(breakB, breakA);
        var breakAInTree = replaced.DescendantNodes().OfType<BreakStatementSyntax>().Single();
        Assert.Null(LabeledJumps.FindTarget(breakAInTree));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindTarget_MiddleOfThreeLoops_ReturnsTheMiddleLoop()
    {
        var method = Method("ThreeLoopsMiddleLabeled");
        var loops = method.DescendantNodes().OfType<ForStatementSyntax>().ToArray();
        var breakMid = method.DescendantNodes().OfType<BreakStatementSyntax>().Single();
        var continueTop = method.DescendantNodes().OfType<ContinueStatementSyntax>().Single();

        Assert.Same(loops[1], LabeledJumps.FindTarget(breakMid));
        Assert.Same(loops[0], LabeledJumps.FindTarget(continueTop));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanTarget_OnlyPlainJumpsNameTheLabel_NeedsNoLabel()
    {
        var plan = LabeledJumps.PlanTarget(Labeled("OnlyPlainJumps", "outer"), s_context.Model);

        Assert.False(plan.NeedsBreakLabel);
        Assert.False(plan.NeedsContinueLabel);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanTarget_IteratorForEach_NeedsNoLabel()
    {
        var plan = LabeledJumps.PlanTarget(Labeled("IteratorForEachLabeled", "outer"), s_context.Model);

        Assert.False(plan.NeedsBreakLabel);
        Assert.False(plan.NeedsContinueLabel);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanTarget_BreakOnlyContinueOnlyAndInnerLabel_SelectsOnlyNeededLabels()
    {
        var breakOnly = LabeledJumps.PlanTarget(Labeled("OnlyBreakOuter", "outer"), s_context.Model);
        Assert.True(breakOnly.NeedsBreakLabel);
        Assert.False(breakOnly.NeedsContinueLabel);

        var continueOnly = LabeledJumps.PlanTarget(Labeled("OnlyContinueOuter", "outer"), s_context.Model);
        Assert.False(continueOnly.NeedsBreakLabel);
        Assert.True(continueOnly.NeedsContinueLabel);

        var outerPlan = LabeledJumps.PlanTarget(Labeled("NestedDifferentLabels", "outer"), s_context.Model);
        var innerPlan = LabeledJumps.PlanTarget(Labeled("NestedDifferentLabels", "inner"), s_context.Model);
        Assert.False(outerPlan.NeedsBreakLabel);
        Assert.False(outerPlan.NeedsContinueLabel);
        Assert.True(innerPlan.NeedsBreakLabel);
        Assert.False(innerPlan.NeedsContinueLabel);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void BranchTargetRewriter_LabeledBreakAndContinue_RewritesMatchingLoopLabelAndPreservesOtherLabel()
    {
        var outerLabeled = Labeled("IteratorBodyWithLabeledJumps", "outer");
        var outerLoop = (ForStatementSyntax)outerLabeled.Statement;
        var rewritten = BranchTargetRewriter.Rewrite(outerLoop.Statement, "end_label", "next_label", loopLabel: "outer");

        var gotos = rewritten.DescendantNodes().OfType<GotoStatementSyntax>().ToArray();
        Assert.Equal(
            ["next_label", "end_label"],
            gotos.Select(static g => ((IdentifierNameSyntax)g.Expression!).Identifier.ValueText));

        var remainingBreaks = rewritten.DescendantNodes().OfType<BreakStatementSyntax>().ToArray();
        var remainingBreak = Assert.Single(remainingBreaks);
        Assert.Equal("other", LabeledJumpSyntax.GetLabelName(remainingBreak)?.Identifier.ValueText);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void HasDynamicControlFlow_LabeledBreakToLoopOutsideUnrollLambda_ReturnsTrue()
    {
        var lambda = Method("UnrollJumpToOuterLoop").DescendantNodes().OfType<LambdaExpressionSyntax>().Single();

        Assert.True(UnrollChecks.HasDynamicControlFlow(lambda.Body));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HasDynamicControlFlow_LabeledBreakToLoopInsideUnrollLambda_ReturnsFalse()
    {
        var lambda = Method("UnrollJumpToInnerLoop").DescendantNodes().OfType<LambdaExpressionSyntax>().Single();

        Assert.False(UnrollChecks.HasDynamicControlFlow(lambda.Body));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MacroLambdaSubstituter_WithoutSemanticModel_PreservesLabeledJumpNameAndSubstitutesValue()
    {
        var lambda = Method("UnrollLabelSameNameAsParameter").DescendantNodes().OfType<SimpleLambdaExpressionSyntax>().Single();
        var substituter = new MacroLambdaSubstituter(
            semanticModel: null,
            loopParam: lambda.Parameter,
            unrollVarName: "__gsharp_unroll_0",
            contextParam: null,
            contextArgExpr: null);
        var rewritten = (BlockSyntax)substituter.Visit(lambda.Body)!;

        var continueStmt = rewritten.DescendantNodes().OfType<ContinueStatementSyntax>().Single();
        var breakStmt = rewritten.DescendantNodes().OfType<BreakStatementSyntax>().Single();
        Assert.Equal("i", LabeledJumpSyntax.GetLabelName(continueStmt)?.Identifier.ValueText);
        Assert.Equal("i", LabeledJumpSyntax.GetLabelName(breakStmt)?.Identifier.ValueText);

        var binary = rewritten.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Single(b => b.IsKind(SyntaxKind.AddExpression));
        Assert.Equal("__gsharp_unroll_0", ((IdentifierNameSyntax)binary.Left).Identifier.ValueText);
    }

    private static MethodDeclarationSyntax Method(string name) =>
        s_context.Tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == name);

    private static LabeledStatementSyntax Labeled(string method, string label) =>
        Method(method)
            .DescendantNodes()
            .OfType<LabeledStatementSyntax>()
            .Single(l => l.Identifier.ValueText == label);
}
