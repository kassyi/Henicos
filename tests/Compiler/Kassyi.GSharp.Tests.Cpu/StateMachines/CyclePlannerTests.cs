#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Unit tests for <see cref="StatementFlow"/> and <see cref="CyclePlanner"/>: natural-loop membership, gate statements
/// right after statements holding backward gotos, constant condition folding, and unplanned forms.
/// </summary>
[Category(TestCategory.Compiler)]
public class CyclePlannerTests
{
    private static BlockSyntax ParseBlock(string code) =>
        (BlockSyntax)SyntaxFactory.ParseStatement($"{{ {code} }}");

    private static bool HoldsSplit(StatementSyntax statement) =>
        statement.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(static invocation => invocation.Expression is IdentifierNameSyntax
            {
                Identifier.ValueText: "Split" or "Mark"
            });

    private static bool? ConstantLiteralCondition(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal
            ? literal.Kind() switch
            {
                SyntaxKind.TrueLiteralExpression => true,
                SyntaxKind.FalseLiteralExpression => false,
                _ => null
            }
            : null;

    private static JumpRegion FindLoopRegion(SyntaxList<StatementSyntax> statements, string label)
    {
        var regionPlan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(regionPlan.Rejected);
        var labelIndex = statements
            .Select(static (statement, index) => (statement, index))
            .Single(entry => entry.statement is LabeledStatementSyntax labeled &&
                             labeled.Identifier.ValueText == label)
            .index;
        return regionPlan.Regions.Single(region =>
            region.Kind == JumpRegionKind.GotoLoop && region.LabelIndex == labelIndex);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_GotoInIf_MarksTheStatementAfterItAsGate()
    {
        var body = ParseBlock("L: Split(); if (a) { if (c) goto L; Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var header = body.Statements[0];
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var innerIf = Assert.IsType<IfStatementSyntax>(thenBlock.Statements[0]);
        var innerSplit = thenBlock.Statements[1];
        var outerSplit = body.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(header), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(outerIf), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(innerIf), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(innerIf.Statement), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(innerSplit), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(outerSplit), cycle.Members);

        Assert.True(plan.IsGateStatement(cycle, innerSplit));
        Assert.True(plan.IsGateStatement(cycle, outerSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoLastInBranch_HasNoGateInsideTheBranch()
    {
        var body = ParseBlock("L: Split(); if (a) { Split(); if (c) goto L; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var innerSplit = thenBlock.Statements[0];
        var outerSplit = body.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(innerSplit), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, innerSplit));
        Assert.True(plan.IsGateStatement(cycle, outerSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_TwoGotos_KeepsTheStatementBetweenThemInTheLoop()
    {
        var body = ParseBlock("L: Split(); if (c) goto L; Split(); if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var middleSplit = body.Statements[2];
        var trailingSplit = body.Statements[4];

        Assert.Contains(StatementFlow.EntryOf(middleSplit), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, middleSplit));
        Assert.True(plan.IsGateStatement(cycle, trailingSplit));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_GotoInBlock_MarksTheStatementAfterItAsGate()
    {
        var body = ParseBlock("L: Split(); { if (c) goto L; Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var innerBlock = Assert.IsType<BlockSyntax>(body.Statements[1]);
        var innerSplit = innerBlock.Statements[1];

        Assert.DoesNotContain(StatementFlow.EntryOf(innerSplit), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, innerSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInLoopBody_IsNotPlanned()
    {
        var body = ParseBlock("L: Split(); for (;;) { if (c) goto L; Split(); }");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInSwitchSection_IsNotPlanned()
    {
        var body = ParseBlock("L: Split(); switch (x) { case 0: if (c) goto L; Split(); break; }");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToLabelInsideTheLastStatement_IsNotPlanned()
    {
        var body = ParseBlock("L: Split(); { if (e) goto X; if (c) goto L; Split(); X: Split(); }");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConstantFalseDoLoop_IsNotALoop()
    {
        var doBody = ParseBlock("do { Split(); } while (false);");
        var doFlow = StatementFlow.Build(doBody, ConstantLiteralCondition);
        var doStatement = Assert.IsType<DoStatementSyntax>(doBody.Statements[0]);
        var doBlock = Assert.IsType<BlockSyntax>(doStatement.Statement);
        var doSplit = doBlock.Statements[0];

        Assert.Empty(doFlow.GetPredecessors(StatementFlow.EntryOf(doStatement)));
        var loopTest = new FlowNode(doStatement, FlowNodeKind.LoopTest);
        Assert.DoesNotContain(StatementFlow.EntryOf(doSplit), doFlow.GetSuccessors(loopTest));
        Assert.Contains(StatementFlow.EndNode, doFlow.GetSuccessors(loopTest));

        var whileBody = ParseBlock("while (true) { Split(); break; }");
        var whileFlow = StatementFlow.Build(whileBody, ConstantLiteralCondition);
        var whileStatement = Assert.IsType<WhileStatementSyntax>(whileBody.Statements[0]);

        Assert.Empty(whileFlow.GetPredecessors(StatementFlow.EntryOf(whileStatement)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_InnerGotoLoop_GatesAfterItsOwnGoto()
    {
        var body = ParseBlock(
            "top: Split(); { again: { if ((t & 2) != 0 && m++ < 1) { goto again; } Mark(30); if ((t & 1) == 1 && n++ < 1) { goto top; } Split(); } } Mark(31);");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        var topCycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "top"));
        Assert.NotNull(topCycle);

        var outerBlock = Assert.IsType<BlockSyntax>(body.Statements[1]);
        var againCycle = plan.Find(outerBlock.Statements, FindLoopRegion(outerBlock.Statements, "again"));
        Assert.NotNull(againCycle);

        var againLabeled = Assert.IsType<LabeledStatementSyntax>(outerBlock.Statements[0]);
        var againBlock = Assert.IsType<BlockSyntax>(againLabeled.Statement);
        var mark30 = againBlock.Statements[1];
        var innerTrailingSplit = againBlock.Statements[3];

        Assert.True(plan.IsGateStatement(againCycle, mark30));
        Assert.False(plan.IsGateStatement(topCycle, mark30));
        Assert.True(plan.IsGateStatement(topCycle, innerTrailingSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToLabelInsideEarlierStatement_IsNotPlanned()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (e) goto X; if (c) goto L; Split(); X: Split(); return; } if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoFromElseToOuterLabelBeforeLast_IsNotPlanned()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Split(); } else { if (e) goto X; return; } X: Split(); goto after; if (d) goto L; after: Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToLabelInsideFirstStatement_IsNotPlanned()
    {
        var body = ParseBlock(
            "L: { if (e) goto X; if (c) goto L; Split(); X: Split(); return; } if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoAfterDoLoopInBranchBeforeLast_IsNotPlanned()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Split(); } else { do { Split(); } while (w); if (e) goto X; return; } X: Split(); goto after; if (d) goto L; after: Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToLabelAfterLastStatement_IsPlanned()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Split(); goto X; } if (d) goto L; X: Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.NotNull(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ExternalGotoIntoSecondChainedLabel_IsNotPlanned()
    {
        var body = ParseBlock("s0(); if (e) goto L1; L0: ; L1: Split(); if (c) goto L0; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L0")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ChainedLabelWithoutExternalEntry_IsPlannedWithBoundedMembers()
    {
        var body = ParseBlock("s0(); L0: ; L1: Split(); if (c) goto L0; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L0"));

        Assert.NotNull(cycle);
        Assert.DoesNotContain(StatementFlow.EntryOf(body.Statements[0]), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(body.Statements[1]), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(body.Statements[2]), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(body.Statements[3]), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, body.Statements[4]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_UnreachableGotoInConstantFalseIf_IsExcludedFromMembers()
    {
        var body = ParseBlock("L: Split(); if (false) { if (c) goto L; Split(); } if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var ifFalse = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var deadBlock = Assert.IsType<BlockSyntax>(ifFalse.Statement);

        Assert.DoesNotContain(StatementFlow.EntryOf(deadBlock), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(deadBlock.Statements[0]), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, deadBlock.Statements[1]));
        Assert.True(plan.IsGateStatement(cycle, body.Statements[3]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NonMemberTwoSlotsAfterLatch_IsNotGate()
    {
        var body = ParseBlock("L: Split(); if (a) { if (c) goto L; Split(); Mark(1); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var gateSplit = thenBlock.Statements[1];
        var markAfterGate = thenBlock.Statements[2];

        Assert.DoesNotContain(StatementFlow.EntryOf(markAfterGate), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, gateSplit));
        Assert.False(plan.IsGateStatement(cycle, markAfterGate));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoAfterGateToOuterLabelBeforeLast_IsNotPlanned()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Split(); goto X; } X: Split(); goto after; if (d) goto L; after: Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoAfterGateInDoLoopToLabelBeforeLast_IsNotPlanned()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Split(); do { if (e) goto X; } while (w); goto after; } X: Split(); goto after; if (d) goto L; after: Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToMemberLabel_KeepsPreJumpGateStatementInMembers()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Split(); goto M; } Split(); M: if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var preJumpSplit = thenBlock.Statements[1];
        var outerSplit = body.Statements[4];

        Assert.Contains(StatementFlow.EntryOf(preJumpSplit), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, preJumpSplit));
        Assert.True(plan.IsGateStatement(cycle, outerSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchWithVarPatternAfterGate_MarksGateAndDoesNotAddFallthrough()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Mark(1); switch (x) { case 0: goto after; case var unused: return; } } if (d) goto L; after: Split();");
        var flow = StatementFlow.Build(body, ConstantLiteralCondition);
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var mark1 = thenBlock.Statements[1];
        var switchStatement = Assert.IsType<SwitchStatementSyntax>(thenBlock.Statements[2]);
        var trailingGotoIf = body.Statements[2];

        Assert.DoesNotContain(StatementFlow.EntryOf(mark1), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, mark1));
        Assert.DoesNotContain(
            StatementFlow.EntryOf(trailingGotoIf),
            flow.GetSuccessors(StatementFlow.EntryOf(switchStatement)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchWithDiscardPatternAfterGate_MarksGateAndDoesNotAddFallthrough()
    {
        var parsed = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Mark(1); switch (x) { case 0: goto after; default: return; } } if (d) goto L; after: Split();");
        var defaultLabel = parsed.DescendantNodes().OfType<DefaultSwitchLabelSyntax>().Single();
        var discardLabel = SyntaxFactory.CasePatternSwitchLabel(
            SyntaxFactory.DiscardPattern(),
            SyntaxFactory.Token(SyntaxKind.ColonToken));
        var body = parsed.ReplaceNode(defaultLabel, discardLabel);

        var flow = StatementFlow.Build(body, ConstantLiteralCondition);
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var mark1 = thenBlock.Statements[1];
        var switchStatement = Assert.IsType<SwitchStatementSyntax>(thenBlock.Statements[2]);
        var trailingGotoIf = body.Statements[2];

        Assert.DoesNotContain(StatementFlow.EntryOf(mark1), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, mark1));
        Assert.DoesNotContain(
            StatementFlow.EntryOf(trailingGotoIf),
            flow.GetSuccessors(StatementFlow.EntryOf(switchStatement)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SwitchWithGuardedVarPatternAfterGate_KeepsFallthroughEdge()
    {
        var body = ParseBlock(
            "L: Split(); if (a) { if (c) goto L; Mark(1); switch (x) { case 0: goto after; case var unused when w: return; } } if (d) goto L; after: Split();");
        var flow = StatementFlow.Build(body, ConstantLiteralCondition);
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var mark1 = thenBlock.Statements[1];
        var switchStatement = Assert.IsType<SwitchStatementSyntax>(thenBlock.Statements[2]);
        var trailingGotoIf = body.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(mark1), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, mark1));
        Assert.Contains(
            StatementFlow.EntryOf(trailingGotoIf),
            flow.GetSuccessors(StatementFlow.EntryOf(switchStatement)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInTryStatement_IsNotPlanned()
    {
        var body = ParseBlock("L: Split(); try { if (c) goto L; } finally { } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);

        Assert.Null(plan.Find(body.Statements, FindLoopRegion(body.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_DoFalseEndingInGoto_ReturnsFalse()
    {
        var body = ParseBlock("do { goto after; } while (false); after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_DoFalseWithContinue_ReturnsTrue()
    {
        var body = ParseBlock("do { if (c) continue; goto after; } while (false); after: Split();");
        Assert.True(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_DoTrueWithContinue_ReturnsFalse()
    {
        var body = ParseBlock("do { if (c) continue; goto after; } while (true); after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_WhileTrueWithUnreachableBreak_ReturnsFalse()
    {
        var body = ParseBlock("while (true) { if (false) break; goto after; } after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_WhileTrueWithReachableBreak_ReturnsTrue()
    {
        var body = ParseBlock("while (true) { if (c) break; goto after; } after: Split();");
        Assert.True(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_ForEverWithoutBreak_ReturnsFalse()
    {
        var body = ParseBlock("for (;;) { goto after; } after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_BlockWithDeadLabelAfterJump_ReturnsFalse()
    {
        var body = ParseBlock("{ if (false) goto dead; goto after; if (a) { dead: Split(); } } after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_BlockWithReachableForwardLabel_ReturnsTrue()
    {
        var body = ParseBlock("{ if (c) goto live; goto after; live: Split(); } after: Split();");
        Assert.True(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_BlockWithGotoToFollowingStatement_ReturnsFalse()
    {
        var body = ParseBlock("if (a) { goto next; } next: Split();");
        var ifStatement = Assert.IsType<IfStatementSyntax>(body.Statements[0]);
        Assert.False(StatementFlow.ReachesEnd(ifStatement.Statement, ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_BlockWithReturnAtMethodEnd_ReturnsFalse()
    {
        var body = ParseBlock("if (a) { return; }");
        var ifStatement = Assert.IsType<IfStatementSyntax>(body.Statements[0]);
        Assert.False(StatementFlow.ReachesEnd(ifStatement.Statement, ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_CheckedBlockWithDeadLabel_ReturnsFalse()
    {
        var body = ParseBlock("checked { goto after; deadChecked: Split(); } after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_SwitchWithUnconditionalVarPattern_ReturnsFalse()
    {
        var body = ParseBlock("switch (x) { case 0: goto after; case var unused: return; } after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_SwitchWithGuardedVarPattern_ReturnsTrue()
    {
        var body = ParseBlock("switch (x) { case 0: goto after; case var unused when w: return; } after: Split();");
        Assert.True(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_SwitchWithUnreachableBreak_ReturnsFalse()
    {
        var body = ParseBlock("switch (x) { case 0: goto after; break; default: return; } after: Split();");
        Assert.False(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesEnd_TryStatementWithGoto_ReturnsTrue()
    {
        var body = ParseBlock("try { goto after; } finally { } after: Split();");
        Assert.True(StatementFlow.ReachesEnd(body.Statements[0], ConstantLiteralCondition));
    }
}
