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
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ElseOfABranchThatOnlyGoesBack_IsGate()
    {
        var body = ParseBlock("L: Split(); if (c) { n++; goto L; } else { Split(); Split(); }");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var ifStatement = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var elseBlock = Assert.IsType<BlockSyntax>(ifStatement.Else!.Statement); // Parsed from the literal above: else exists
        Assert.True(plan.IsGateStatement(cycle, elseBlock.Statements[0]));
        Assert.False(plan.IsGateStatement(cycle, elseBlock.Statements[1]));

        var unbraced = ParseBlock("L: Split(); if (c) { n++; goto L; } else Split();");
        var unbracedPlan = CyclePlanner.Plan(unbraced, HoldsSplit, ConstantLiteralCondition);
        var unbracedCycle = unbracedPlan.Find(unbraced.Statements, FindLoopRegion(unbraced.Statements, "L"));

        Assert.NotNull(unbracedCycle);
        var unbracedIf = Assert.IsType<IfStatementSyntax>(unbraced.Statements[1]);
        Assert.True(unbracedPlan.IsGateStatement(unbracedCycle, unbracedIf.Else!.Statement)); // Parsed from the literal above: else exists
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_LabeledStatementInBranchThatOnlyGoesBack_ElseIsGate()
    {
        var body = ParseBlock("L: Split(); if (c) { M: n++; if (n > 100) goto M; goto L; } else { Split(); }");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var ifStatement = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var elseBlock = Assert.IsType<BlockSyntax>(ifStatement.Else!.Statement); // Parsed from the literal above: else exists
        Assert.True(plan.IsGateStatement(cycle, elseBlock.Statements[0]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ElseOfABranchThatFallsThrough_IsNotGate()
    {
        var body = ParseBlock("L: Split(); if (c) { if (d) goto L; } else { Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var ifStatement = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var elseBlock = Assert.IsType<BlockSyntax>(ifStatement.Else!.Statement); // Parsed from the literal above: else exists
        Assert.False(plan.IsGateStatement(cycle, elseBlock.Statements[0]));
        Assert.True(plan.IsGateStatement(cycle, body.Statements[2]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_IfWithoutElseAfterBackGoto_HasNoBranchGate()
    {
        var body = ParseBlock("L: Split(); if (c) { n++; goto L; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var ifStatement = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(ifStatement.Statement);
        Assert.False(plan.IsGateStatement(cycle, thenBlock.Statements[0]));
        Assert.False(plan.IsGateStatement(cycle, thenBlock.Statements[1]));
        Assert.True(plan.IsGateStatement(cycle, body.Statements[2]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInForBody_IsPlannedAndNeedsLatch()
    {
        var body = ParseBlock("L: Split(); for (int i = 0; i < u; i++) { if (c) goto L; Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Single(cycle.BackJumps);
        Assert.True(cycle.NeedsLatch);

        var forStatement = Assert.IsType<ForStatementSyntax>(body.Statements[1]);
        var forBody = Assert.IsType<BlockSyntax>(forStatement.Statement);
        var insideSplit = forBody.Statements[1];
        var afterSplit = body.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(insideSplit), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, insideSplit));
        Assert.True(plan.IsGateStatement(cycle, afterSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInSwitchSection_IsPlanned()
    {
        var body = ParseBlock("L: Split(); switch (x) { case 0: if (c) goto L; Split(); break; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.False(cycle.NeedsLatch);

        var switchStatement = Assert.IsType<SwitchStatementSyntax>(body.Statements[1]);
        var sectionSplit = switchStatement.Sections[0].Statements[1];
        var afterSwitchSplit = body.Statements[2];

        Assert.DoesNotContain(StatementFlow.EntryOf(sectionSplit), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, sectionSplit));
        Assert.True(plan.IsGateStatement(cycle, afterSwitchSplit));
        Assert.Equal(new[] { cycle }, plan.FindCyclesGatedInside(switchStatement));
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
    public void Plan_ForwardGotoIntoSecondChainedLabel_IsPlannedWithBoundedMembers()
    {
        var body = ParseBlock("s0(); if (e) goto L1; L0: ; L1: Split(); if (c) goto L0; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L0"));

        Assert.NotNull(cycle);
        Assert.DoesNotContain(StatementFlow.EntryOf(body.Statements[0]), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(body.Statements[1]), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(body.Statements[2]), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(body.Statements[3]), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(body.Statements[4]), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, body.Statements[5]));
        Assert.False(cycle.NeedsLatch);
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

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_TwoGotos_NeedsLatch()
    {
        var body = ParseBlock("L: Split(); if (c) goto L; Split(); if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Equal(2, cycle.BackJumps.Length);
        Assert.True(cycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SplitInElseThatFallsThrough_NeedsLatch()
    {
        var body = ParseBlock("L: Split(); if (a) { if (c) goto L; Split(); } else { Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Single(cycle.BackJumps);
        Assert.True(cycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_GotoInIf_NeedsNoLatch()
    {
        var body = ParseBlock("L: Split(); if (a) { if (c) goto L; Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Single(cycle.BackJumps);
        Assert.False(cycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ElseOfABranchThatOnlyGoesBack_NeedsNoLatch()
    {
        var body = ParseBlock("L: Split(); if (c) { n++; goto L; } else { Split(); Split(); }");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Single(cycle.BackJumps);
        Assert.False(cycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_OneGoto_NeedsNoLatch()
    {
        var body = ParseBlock("L: Split(); if (c) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Single(cycle.BackJumps);
        Assert.False(cycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoIntoSecondChainedHeaderLabel_IsPlannedAndNeedsLatch()
    {
        var body = ParseBlock(
            "if (a) goto L1; L0: ; L1: ; Split(); if (c) goto L0; Split(); if (d) goto L1; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L0"));

        Assert.NotNull(cycle);
        Assert.Equal(2, cycle.BackJumps.Length);
        Assert.True(cycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInBreakBlockOfInnerGotoLoop_NeedsLatch()
    {
        var body = ParseBlock(
            "L0: Split(); { L1: Split(); if (a) { Split(); if (c) goto L0; return; } if (d) goto L1; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle0 = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L0"));
        var innerBlock = Assert.IsType<BlockSyntax>(body.Statements[1]);
        var cycle1 = plan.Find(innerBlock.Statements, FindLoopRegion(innerBlock.Statements, "L1"));

        Assert.NotNull(cycle0);
        Assert.Single(cycle0.BackJumps);
        Assert.True(cycle0.NeedsLatch);

        Assert.NotNull(cycle1);
        Assert.Single(cycle1.BackJumps);
        // The split in the early exit block is outside the cycle and before every gate, so the inner loop needs a latch
        Assert.True(cycle1.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInConstantFalseDoBody_IsPlannedWithoutLatch()
    {
        var body = ParseBlock("L: Split(); do { if (c) goto L; Split(); } while (false); Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        Assert.Single(cycle.BackJumps);
        Assert.False(cycle.NeedsLatch);

        var doStatement = Assert.IsType<DoStatementSyntax>(body.Statements[1]);
        var doBody = Assert.IsType<BlockSyntax>(doStatement.Statement);
        Assert.True(plan.IsGateStatement(cycle, doBody.Statements[1]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GateInLoopWithBreakButNoBackEdge_IsNotPlanned()
    {
        var doWithBreak = ParseBlock("L: Split(); do { if (c) goto L; Split(); break; } while (false); Split();");
        var doPlan = CyclePlanner.Plan(doWithBreak, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(doPlan.Find(doWithBreak.Statements, FindLoopRegion(doWithBreak.Statements, "L")));

        var whileWithBreak = ParseBlock(
            "L: Split(); while (true) { if (a) { if (c) goto L; Split(); } break; } Split();");
        var whilePlan = CyclePlanner.Plan(whileWithBreak, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(whilePlan.Find(whileWithBreak.Statements, FindLoopRegion(whileWithBreak.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindCyclesGatedInside_GateInBreakBlock_ReturnsTheGotoLoop()
    {
        var body = ParseBlock(
            "L: Split(); for (int i = 0; i < u; i++) { if (a) { if (c) goto L; Split(); break; } Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));
        Assert.NotNull(cycle);

        var forStatement = Assert.IsType<ForStatementSyntax>(body.Statements[1]);
        Assert.Equal(new[] { cycle }, plan.FindCyclesGatedInside(forStatement));

        var withoutGate = ParseBlock(
            "L: Split(); for (int i = 0; i < u; i++) { if (c) goto L; Split(); } Split();");
        var withoutGatePlan = CyclePlanner.Plan(withoutGate, HoldsSplit, ConstantLiteralCondition);
        var withoutGateFor = Assert.IsType<ForStatementSyntax>(withoutGate.Statements[1]);
        Assert.Empty(withoutGatePlan.FindCyclesGatedInside(withoutGateFor));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindCyclesGatedInside_LoopWithEarlyExitGateWhoseNormalExitStaysInCycle_ReturnsEmpty()
    {
        var singleLoopBody = ParseBlock(
            "L: Split(); for (int i = 0; i < u; i++) { if (a) { if (c) goto L; Split(); return; } } if (d) goto L; Split();");
        var singleLoopPlan = CyclePlanner.Plan(singleLoopBody, HoldsSplit, ConstantLiteralCondition);
        var singleLoopCycle = singleLoopPlan.Find(
            singleLoopBody.Statements, FindLoopRegion(singleLoopBody.Statements, "L"));
        Assert.NotNull(singleLoopCycle);

        var singleFor = Assert.IsType<ForStatementSyntax>(singleLoopBody.Statements[1]);
        var singleForBody = Assert.IsType<BlockSyntax>(singleFor.Statement);
        var singleOuterIf = Assert.IsType<IfStatementSyntax>(singleForBody.Statements[0]);
        var singleIfBody = Assert.IsType<BlockSyntax>(singleOuterIf.Statement);
        var singleGateSplit = singleIfBody.Statements[1];

        Assert.Contains(StatementFlow.EntryOf(singleLoopBody.Statements[2]), singleLoopCycle.Members);
        Assert.True(singleLoopPlan.IsGateStatement(singleLoopCycle, singleGateSplit));
        Assert.Empty(singleLoopPlan.FindCyclesGatedInside(singleFor));

        var nestedLoopBody = ParseBlock(
            "L: Split(); for (int i = 0; i < u; i++) { for (int j = 0; j < u; j++) { if (a) { if (c) goto L; Split(); goto afterOuter; } } Split(); } afterOuter: Split();");
        var nestedPlan = CyclePlanner.Plan(nestedLoopBody, HoldsSplit, ConstantLiteralCondition);
        var nestedCycle = nestedPlan.Find(
            nestedLoopBody.Statements, FindLoopRegion(nestedLoopBody.Statements, "L"));
        Assert.NotNull(nestedCycle);

        var outerFor = Assert.IsType<ForStatementSyntax>(nestedLoopBody.Statements[1]);
        var outerBody = Assert.IsType<BlockSyntax>(outerFor.Statement);
        var innerFor = Assert.IsType<ForStatementSyntax>(outerBody.Statements[0]);
        var outerSplit = outerBody.Statements[1];
        var afterOuterSplit = nestedLoopBody.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(outerSplit), nestedCycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(afterOuterSplit), nestedCycle.Members);
        Assert.Empty(nestedPlan.FindCyclesGatedInside(innerFor));
        Assert.Equal(new[] { nestedCycle }, nestedPlan.FindCyclesGatedInside(outerFor));

        var doubleLoopBody = ParseBlock(
            "L0: Split(); L1: Split(); for (int i = 0; i < u; i++) { if (a) { if (c) goto L0; if (b) goto L1; Split(); return; } } Split(); if (d) goto L0; Split();");
        var doublePlan = CyclePlanner.Plan(doubleLoopBody, HoldsSplit, ConstantLiteralCondition);
        var outerCycle = doublePlan.Find(doubleLoopBody.Statements, FindLoopRegion(doubleLoopBody.Statements, "L0"));
        var innerCycle = doublePlan.Find(doubleLoopBody.Statements, FindLoopRegion(doubleLoopBody.Statements, "L1"));
        Assert.NotNull(outerCycle);
        Assert.NotNull(innerCycle);

        var guardedFor = Assert.IsType<ForStatementSyntax>(doubleLoopBody.Statements[2]);
        Assert.Equal(new[] { innerCycle }, doublePlan.FindCyclesGatedInside(guardedFor));

        var doInCycleBody = ParseBlock(
            "L: Split(); do { for (int i = 0; i < u; i++) { if (a) { if (c) goto L; Split(); return; } } if (d) goto L; break; } while (false); if (e) goto L; Split();");
        var doInCyclePlan = CyclePlanner.Plan(doInCycleBody, HoldsSplit, ConstantLiteralCondition);
        Assert.NotNull(doInCyclePlan.Find(doInCycleBody.Statements, FindLoopRegion(doInCycleBody.Statements, "L")));

        var doExitsCycleBody = ParseBlock(
            "L: Split(); do { for (int i = 0; i < u; i++) { if (a) { if (c) goto L; Split(); return; } } if (d) goto L; break; } while (false); Split();");
        var doExitsCyclePlan = CyclePlanner.Plan(doExitsCycleBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(doExitsCyclePlan.Find(doExitsCycleBody.Statements, FindLoopRegion(doExitsCycleBody.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInWhileOrForEachBody_IsPlannedAndNeedsLatch()
    {
        var whileBody = ParseBlock("L: Split(); while (k < u) { if (c) goto L; Split(); } Split();");
        var whilePlan = CyclePlanner.Plan(whileBody, HoldsSplit, ConstantLiteralCondition);
        var whileCycle = whilePlan.Find(whileBody.Statements, FindLoopRegion(whileBody.Statements, "L"));

        Assert.NotNull(whileCycle);
        Assert.Single(whileCycle.BackJumps);
        Assert.True(whileCycle.NeedsLatch);

        var whileStatement = Assert.IsType<WhileStatementSyntax>(whileBody.Statements[1]);
        var whileBlock = Assert.IsType<BlockSyntax>(whileStatement.Statement);
        Assert.Contains(StatementFlow.EntryOf(whileBlock.Statements[1]), whileCycle.Members);
        Assert.False(whilePlan.IsGateStatement(whileCycle, whileBlock.Statements[1]));
        Assert.True(whilePlan.IsGateStatement(whileCycle, whileBody.Statements[2]));
        Assert.Empty(whilePlan.FindCyclesGatedInside(whileStatement));

        var forEachBody = ParseBlock("L: Split(); foreach (var item in items) { if (c) goto L; Split(); } Split();");
        var forEachPlan = CyclePlanner.Plan(forEachBody, HoldsSplit, ConstantLiteralCondition);
        var forEachCycle = forEachPlan.Find(forEachBody.Statements, FindLoopRegion(forEachBody.Statements, "L"));

        Assert.NotNull(forEachCycle);
        Assert.Single(forEachCycle.BackJumps);
        Assert.True(forEachCycle.NeedsLatch);

        var forEachStatement = Assert.IsType<ForEachStatementSyntax>(forEachBody.Statements[1]);
        var forEachBlock = Assert.IsType<BlockSyntax>(forEachStatement.Statement);
        Assert.Contains(StatementFlow.EntryOf(forEachBlock.Statements[1]), forEachCycle.Members);
        Assert.False(forEachPlan.IsGateStatement(forEachCycle, forEachBlock.Statements[1]));
        Assert.True(forEachPlan.IsGateStatement(forEachCycle, forEachBody.Statements[2]));
        Assert.Empty(forEachPlan.FindCyclesGatedInside(forEachStatement));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ContinueBetweenGotoAndLaterStatementInFor_MarksNoGate()
    {
        var body = ParseBlock(
            "L1: Split(); for (int i = 0; i < u; i++) { if (a) { if (c) goto L1; if (i == 0) continue; Mark(1); break; } Split(); } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L1"));

        Assert.NotNull(cycle);
        var forStatement = Assert.IsType<ForStatementSyntax>(body.Statements[1]);
        var forBody = Assert.IsType<BlockSyntax>(forStatement.Statement);
        var outerIf = Assert.IsType<IfStatementSyntax>(forBody.Statements[0]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var continueIf = thenBlock.Statements[1];
        var gateMark = thenBlock.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(continueIf), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(gateMark), cycle.Members);
        Assert.False(plan.IsLatchStatement(cycle, continueIf));
        Assert.False(plan.IsGateStatement(cycle, gateMark));
        Assert.Empty(plan.FindCyclesGatedInside(forStatement));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_LabeledBreakIntoCycleBetweenGotoAndGate_MarksNoGate()
    {
        var body = ParseBlock(
            "L1: Split(); outer: for (int i = 0; i < u; i++) { for (int j = 0; j < u; j++) { if (a) { if (c) goto L1; if (b) break outer; Mark(1); return; } } Mark(2); return; } if (d) goto L1; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L1"));

        Assert.NotNull(cycle);
        var outerLabeled = Assert.IsType<LabeledStatementSyntax>(body.Statements[1]);
        var outerFor = Assert.IsType<ForStatementSyntax>(outerLabeled.Statement);
        var outerBody = Assert.IsType<BlockSyntax>(outerFor.Statement);
        var innerFor = Assert.IsType<ForStatementSyntax>(outerBody.Statements[0]);
        var innerBody = Assert.IsType<BlockSyntax>(innerFor.Statement);
        var outerIf = Assert.IsType<IfStatementSyntax>(innerBody.Statements[0]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var breakOuterIf = thenBlock.Statements[1];
        var mark1 = thenBlock.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(breakOuterIf), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(mark1), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, mark1));
        Assert.Empty(plan.FindCyclesGatedInside(innerFor));
        Assert.Empty(plan.FindCyclesGatedInside(outerFor));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NestedGotoLoopsWhereForExitsInnerOnly_MarksInnerGateOnly()
    {
        var plainBody = ParseBlock(
            "L0: Split(); L1: for (int i = 0; i < u; i++) { if (a) { if (c) goto L0; if (b) goto L1; Mark(1); return; } } Split(); if (d) goto L0; Split();");
        var plainPlan = CyclePlanner.Plan(plainBody, HoldsSplit, ConstantLiteralCondition);
        var plainCycle0 = plainPlan.Find(plainBody.Statements, FindLoopRegion(plainBody.Statements, "L0"));
        var plainCycle1 = plainPlan.Find(plainBody.Statements, FindLoopRegion(plainBody.Statements, "L1"));

        Assert.NotNull(plainCycle0);
        Assert.NotNull(plainCycle1);
        var plainLabeled = Assert.IsType<LabeledStatementSyntax>(plainBody.Statements[1]);
        var plainFor = Assert.IsType<ForStatementSyntax>(plainLabeled.Statement);
        var plainForBody = Assert.IsType<BlockSyntax>(plainFor.Statement);
        var plainIf = Assert.IsType<IfStatementSyntax>(plainForBody.Statements[0]);
        var plainThen = Assert.IsType<BlockSyntax>(plainIf.Statement);
        var plainGotoL1If = plainThen.Statements[1];
        var plainMark1 = plainThen.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(plainGotoL1If), plainCycle0.Members);
        Assert.Contains(StatementFlow.EntryOf(plainGotoL1If), plainCycle1.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(plainMark1), plainCycle0.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(plainMark1), plainCycle1.Members);
        Assert.False(plainPlan.IsGateStatement(plainCycle0, plainMark1));
        Assert.True(plainPlan.IsGateStatement(plainCycle1, plainMark1));
        Assert.Equal(new[] { plainCycle1 }, plainPlan.FindCyclesGatedInside(plainFor));

        var loopBody = ParseBlock(
            "L0: Split(); L1: for (int i = 0; i < u; i++) { if (i == 0) continue; if (e) break; if (c) goto L0; if (b) goto L1; Mark(1); return; } Split(); if (d) goto L0; Split();");
        var loopPlan = CyclePlanner.Plan(loopBody, HoldsSplit, ConstantLiteralCondition);
        var loopCycle0 = loopPlan.Find(loopBody.Statements, FindLoopRegion(loopBody.Statements, "L0"));
        var loopCycle1 = loopPlan.Find(loopBody.Statements, FindLoopRegion(loopBody.Statements, "L1"));

        Assert.NotNull(loopCycle0);
        Assert.NotNull(loopCycle1);
        var loopLabeled = Assert.IsType<LabeledStatementSyntax>(loopBody.Statements[1]);
        var loopFor = Assert.IsType<ForStatementSyntax>(loopLabeled.Statement);
        var loopForBody = Assert.IsType<BlockSyntax>(loopFor.Statement);
        var loopMark1 = loopForBody.Statements[4];

        Assert.False(loopPlan.IsGateStatement(loopCycle0, loopMark1));
        Assert.True(loopPlan.IsGateStatement(loopCycle1, loopMark1));
        Assert.Equal(new[] { loopCycle1 }, loopPlan.FindCyclesGatedInside(loopFor));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConditionalForwardGotoToMemberLabelBeforeNonMember_DoesNotMarkNonMemberAsGate()
    {
        var body = ParseBlock(
            "L1: Split(); if (a) { if (c) goto L1; if (b) goto M; Mark(1); return; } M: if (d) goto L1; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L1"));

        Assert.NotNull(cycle);
        var outerIf = Assert.IsType<IfStatementSyntax>(body.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var forwardGotoIf = thenBlock.Statements[1];
        var mark1 = thenBlock.Statements[2];
        var memberLabel = body.Statements[2];

        Assert.Contains(StatementFlow.EntryOf(forwardGotoIf), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(memberLabel), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(mark1), cycle.Members);
        Assert.False(plan.IsGateStatement(cycle, mark1));
        Assert.True(plan.IsGateStatement(cycle, body.Statements[3]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_DeadStatementAfterUnconditionalJumpInCycle_IsNotMarkedAsGate()
    {
        var afterGotoBody = ParseBlock(
            "L1: Split(); if (a) { if (c) goto L1; goto M; Mark(1); } M: if (d) goto L1; Split();");
        var afterGotoPlan = CyclePlanner.Plan(afterGotoBody, HoldsSplit, ConstantLiteralCondition);
        var afterGotoCycle = afterGotoPlan.Find(afterGotoBody.Statements, FindLoopRegion(afterGotoBody.Statements, "L1"));
        Assert.NotNull(afterGotoCycle);
        var afterGotoIf = Assert.IsType<IfStatementSyntax>(afterGotoBody.Statements[1]);
        var afterGotoThen = Assert.IsType<BlockSyntax>(afterGotoIf.Statement);
        var deadAfterGoto = afterGotoThen.Statements[2];
        Assert.DoesNotContain(StatementFlow.EntryOf(deadAfterGoto), afterGotoCycle.Members);
        Assert.False(afterGotoPlan.IsGateStatement(afterGotoCycle, deadAfterGoto));

        var afterContinueBody = ParseBlock(
            "L1: Split(); for (int i = 0; i < u; i++) { if (a) { if (c) goto L1; continue; Mark(1); } Split(); } Split();");
        var afterContinuePlan = CyclePlanner.Plan(afterContinueBody, HoldsSplit, ConstantLiteralCondition);
        var afterContinueCycle = afterContinuePlan.Find(
            afterContinueBody.Statements, FindLoopRegion(afterContinueBody.Statements, "L1"));
        Assert.NotNull(afterContinueCycle);
        var forStatement = Assert.IsType<ForStatementSyntax>(afterContinueBody.Statements[1]);
        var forBody = Assert.IsType<BlockSyntax>(forStatement.Statement);
        var afterContinueIf = Assert.IsType<IfStatementSyntax>(forBody.Statements[0]);
        var afterContinueThen = Assert.IsType<BlockSyntax>(afterContinueIf.Statement);
        var deadAfterContinue = afterContinueThen.Statements[2];
        Assert.DoesNotContain(StatementFlow.EntryOf(deadAfterContinue), afterContinueCycle.Members);
        Assert.False(afterContinuePlan.IsGateStatement(afterContinueCycle, deadAfterContinue));
        Assert.Empty(afterContinuePlan.FindCyclesGatedInside(forStatement));

        var afterIfElseBody = ParseBlock(
            "L1: Split(); if (a) { if (c) goto L1; else return; Mark(1); } if (d) goto L1; Split();");
        var afterIfElsePlan = CyclePlanner.Plan(afterIfElseBody, HoldsSplit, ConstantLiteralCondition);
        var afterIfElseCycle = afterIfElsePlan.Find(
            afterIfElseBody.Statements, FindLoopRegion(afterIfElseBody.Statements, "L1"));
        Assert.NotNull(afterIfElseCycle);
        var outerIfElse = Assert.IsType<IfStatementSyntax>(afterIfElseBody.Statements[1]);
        var outerIfElseThen = Assert.IsType<BlockSyntax>(outerIfElse.Statement);
        var deadAfterIfElse = outerIfElseThen.Statements[1];
        Assert.DoesNotContain(StatementFlow.EntryOf(deadAfterIfElse), afterIfElseCycle.Members);
        Assert.False(afterIfElsePlan.IsGateStatement(afterIfElseCycle, deadAfterIfElse));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_StatementAfterForwardGotoInSwitchSectionOrDoLoop_IsPlannedWithoutThatGate()
    {
        var switchBody = ParseBlock(
            "L1: Split(); switch (x) { case 0: if (b) goto M; Mark(1); return; } if (c) goto L1; M: if (d) goto L1; Split();");
        var switchPlan = CyclePlanner.Plan(switchBody, HoldsSplit, ConstantLiteralCondition);
        var switchCycle = switchPlan.Find(switchBody.Statements, FindLoopRegion(switchBody.Statements, "L1"));
        Assert.NotNull(switchCycle);
        var switchStatement = Assert.IsType<SwitchStatementSyntax>(switchBody.Statements[1]);
        Assert.False(switchPlan.IsGateStatement(switchCycle, switchStatement.Sections[0].Statements[1]));
        Assert.True(switchPlan.IsGateStatement(switchCycle, switchBody.Statements[4]));

        var doBody = ParseBlock(
            "L1: Split(); do { if (a) { if (b) goto M; Mark(1); return; } if (d) break; if (c) goto L1; } while (false); Mark(2); return; M: if (e) goto L1; Split();");
        var doPlan = CyclePlanner.Plan(doBody, HoldsSplit, ConstantLiteralCondition);
        var doCycle = doPlan.Find(doBody.Statements, FindLoopRegion(doBody.Statements, "L1"));
        Assert.NotNull(doCycle);
        var doStatement = Assert.IsType<DoStatementSyntax>(doBody.Statements[1]);
        var doBlock = Assert.IsType<BlockSyntax>(doStatement.Statement);
        var doIf = Assert.IsType<IfStatementSyntax>(doBlock.Statements[0]);
        var doThen = Assert.IsType<BlockSyntax>(doIf.Statement);
        Assert.False(doPlan.IsGateStatement(doCycle, doThen.Statements[1]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInSwitchWithJoins_IsNotPlanned()
    {
        var gotoCaseJoinBody = ParseBlock(
            "L: Split(); switch (x) { case 0: if (c) goto L; Split(); goto case 1; case 1: Split(); break; } Split();");
        var gotoCaseJoinPlan = CyclePlanner.Plan(gotoCaseJoinBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(gotoCaseJoinPlan.Find(gotoCaseJoinBody.Statements, FindLoopRegion(gotoCaseJoinBody.Statements, "L")));

        var gotoLabelJoinBody = ParseBlock(
            "L: Split(); switch (x) { case 0: if (c) goto L; Split(); goto target; case 1: target: Split(); break; } Split();");
        var gotoLabelJoinPlan = CyclePlanner.Plan(gotoLabelJoinBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(gotoLabelJoinPlan.Find(gotoLabelJoinBody.Statements, FindLoopRegion(gotoLabelJoinBody.Statements, "L")));

        var gotoDefaultJoinBody = ParseBlock(
            "L: Split(); switch (x) { case 0: if (c) goto L; Split(); goto default; default: Split(); break; } Split();");
        var gotoDefaultJoinPlan = CyclePlanner.Plan(gotoDefaultJoinBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(gotoDefaultJoinPlan.Find(gotoDefaultJoinBody.Statements, FindLoopRegion(gotoDefaultJoinBody.Statements, "L")));

        var uniformGotoCaseJoinBody = ParseBlock(
            "L: Split(); switch (u) { case 0: if (c) goto L; goto case 1; case 1: Mark(1); break; } Split();");
        var uniformGotoCaseJoinPlan = CyclePlanner.Plan(uniformGotoCaseJoinBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(uniformGotoCaseJoinPlan.Find(
            uniformGotoCaseJoinBody.Statements, FindLoopRegion(uniformGotoCaseJoinBody.Statements, "L")));

        var unresolvedGotoCaseBody = ParseBlock(
            "L: Split(); switch (x) { case K: if (c) goto L; Mark(1); goto case 1; default: break; } Split();");
        var unresolvedGotoCasePlan = CyclePlanner.Plan(unresolvedGotoCaseBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(unresolvedGotoCasePlan.Find(
            unresolvedGotoCaseBody.Statements, FindLoopRegion(unresolvedGotoCaseBody.Statements, "L")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindCyclesGatedInside_SwitchWithDefaultAndWithoutDefault_ReturnsCycle()
    {
        var withDefaultBody = ParseBlock(
            "L: Split(); switch (x) { case 0: if (c) goto L; Split(); break; default: break; } Split();");
        var withDefaultPlan = CyclePlanner.Plan(withDefaultBody, HoldsSplit, ConstantLiteralCondition);
        var withDefaultCycle = withDefaultPlan.Find(
            withDefaultBody.Statements, FindLoopRegion(withDefaultBody.Statements, "L"));
        Assert.NotNull(withDefaultCycle);
        var withDefaultSwitch = Assert.IsType<SwitchStatementSyntax>(withDefaultBody.Statements[1]);
        Assert.Equal(new[] { withDefaultCycle }, withDefaultPlan.FindCyclesGatedInside(withDefaultSwitch));

        var withoutDefaultBody = ParseBlock(
            "L: Split(); switch (x) { case 0: if (c) goto L; Split(); break; } Split();");
        var withoutDefaultPlan = CyclePlanner.Plan(withoutDefaultBody, HoldsSplit, ConstantLiteralCondition);
        var withoutDefaultCycle = withoutDefaultPlan.Find(
            withoutDefaultBody.Statements, FindLoopRegion(withoutDefaultBody.Statements, "L"));
        Assert.NotNull(withoutDefaultCycle);
        var withoutDefaultSwitch = Assert.IsType<SwitchStatementSyntax>(withoutDefaultBody.Statements[1]);
        Assert.Equal(new[] { withoutDefaultCycle }, withoutDefaultPlan.FindCyclesGatedInside(withoutDefaultSwitch));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInSwitchWithWhenOrPattern_MarksGateAndFindsEnclosingCycle()
    {
        var whenBody = ParseBlock(
            "L: Split(); switch (x) { case int v when w: if (c) goto L; Split(); break; } Split();");
        var whenPlan = CyclePlanner.Plan(whenBody, HoldsSplit, ConstantLiteralCondition);
        var whenCycle = whenPlan.Find(whenBody.Statements, FindLoopRegion(whenBody.Statements, "L"));
        Assert.NotNull(whenCycle);
        var whenSwitch = Assert.IsType<SwitchStatementSyntax>(whenBody.Statements[1]);
        var whenGate = whenSwitch.Sections[0].Statements[1];
        Assert.True(whenPlan.IsGateStatement(whenCycle, whenGate));
        Assert.True(whenPlan.IsGateStatement(whenCycle, whenBody.Statements[2]));
        Assert.Equal(new[] { whenCycle }, whenPlan.FindCyclesGatedInside(whenSwitch));

        var patternBody = ParseBlock(
            "L: Split(); switch (x) { case > 0: if (c) goto L; Split(); break; } Split();");
        var patternPlan = CyclePlanner.Plan(patternBody, HoldsSplit, ConstantLiteralCondition);
        var patternCycle = patternPlan.Find(patternBody.Statements, FindLoopRegion(patternBody.Statements, "L"));
        Assert.NotNull(patternCycle);
        var patternSwitch = Assert.IsType<SwitchStatementSyntax>(patternBody.Statements[1]);
        Assert.True(patternPlan.IsGateStatement(patternCycle, patternSwitch.Sections[0].Statements[1]));
        Assert.Equal(new[] { patternCycle }, patternPlan.FindCyclesGatedInside(patternSwitch));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInSwitchWhereBreakStaysInCycle_ReturnsEmptyForFindCyclesGatedInside()
    {
        var body = ParseBlock(
            "L: Split(); switch (x) { case 0: if (a) { if (c) goto L; Split(); return; } break; } if (d) goto L; Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var switchStatement = Assert.IsType<SwitchStatementSyntax>(body.Statements[1]);
        var ifStatement = Assert.IsType<IfStatementSyntax>(switchStatement.Sections[0].Statements[0]);
        var thenBlock = Assert.IsType<BlockSyntax>(ifStatement.Statement);
        var innerSplit = thenBlock.Statements[1];

        Assert.Contains(StatementFlow.EntryOf(body.Statements[2]), cycle.Members);
        Assert.True(plan.IsGateStatement(cycle, innerSplit));
        Assert.Empty(plan.FindCyclesGatedInside(switchStatement));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInUniformSwitchSection_IsPlanned()
    {
        var body = ParseBlock(
            "L: Split(); switch (u) { case 0: if (c) goto L; Split(); break; default: break; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var cycle = plan.Find(body.Statements, FindLoopRegion(body.Statements, "L"));

        Assert.NotNull(cycle);
        var switchStatement = Assert.IsType<SwitchStatementSyntax>(body.Statements[1]);
        Assert.True(plan.IsGateStatement(cycle, switchStatement.Sections[0].Statements[1]));
        Assert.Equal(new[] { cycle }, plan.FindCyclesGatedInside(switchStatement));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoInSwitchSectionWithSelfGotoCase_NeedsLatch()
    {
        var selfCaseBody = ParseBlock(
            "L: Split(); switch (x) { case 0: Split(); if (a) goto case 0; if (b) goto L; Split(); break; default: break; } Split();");
        var selfCasePlan = CyclePlanner.Plan(selfCaseBody, HoldsSplit, ConstantLiteralCondition);
        var selfCaseCycle = selfCasePlan.Find(
            selfCaseBody.Statements, FindLoopRegion(selfCaseBody.Statements, "L"));

        Assert.NotNull(selfCaseCycle);
        Assert.Single(selfCaseCycle.BackJumps);
        Assert.True(selfCaseCycle.NeedsLatch);

        var selfDefaultBody = ParseBlock(
            "L: Split(); switch (x) { default: Split(); if (a) goto default; if (b) goto L; Split(); break; } Split();");
        var selfDefaultPlan = CyclePlanner.Plan(selfDefaultBody, HoldsSplit, ConstantLiteralCondition);
        var selfDefaultCycle = selfDefaultPlan.Find(
            selfDefaultBody.Statements, FindLoopRegion(selfDefaultBody.Statements, "L"));

        Assert.NotNull(selfDefaultCycle);
        Assert.Single(selfDefaultCycle.BackJumps);
        Assert.True(selfDefaultCycle.NeedsLatch);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_StatementAfterContinueThatLeavesTheLoop_IsGate()
    {
        var body = ParseBlock("while (true) { Split(); if (a) { if (c) continue; Split(); } break; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var whileStatement = Assert.IsType<WhileStatementSyntax>(body.Statements[0]);
        var cycle = plan.FindLoop(whileStatement);

        Assert.NotNull(cycle);
        var loopBlock = Assert.IsType<BlockSyntax>(whileStatement.Statement);
        var firstSplit = loopBlock.Statements[0];
        var outerIf = Assert.IsType<IfStatementSyntax>(loopBlock.Statements[1]);
        var thenBlock = Assert.IsType<BlockSyntax>(outerIf.Statement);
        var innerIf = thenBlock.Statements[0];
        var innerSplit = thenBlock.Statements[1];
        var breakStatement = loopBlock.Statements[2];
        var trailingSplit = body.Statements[1];

        Assert.Contains(StatementFlow.EntryOf(whileStatement), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(firstSplit), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(outerIf), cycle.Members);
        Assert.Contains(StatementFlow.EntryOf(innerIf), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(innerSplit), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(breakStatement), cycle.Members);
        Assert.DoesNotContain(StatementFlow.EntryOf(trailingSplit), cycle.Members);

        Assert.True(plan.IsGateStatement(cycle, innerSplit));
        Assert.True(plan.IsGateStatement(cycle, breakStatement));
        Assert.False(plan.IsGateStatement(cycle, trailingSplit));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_BreakBlock_IsNotGate()
    {
        var pureBreakBody = ParseBlock("for (int i = 0; i < u; i++) { Split(); if (a) { Split(); break; } } Split();");
        var pureBreakPlan = CyclePlanner.Plan(pureBreakBody, HoldsSplit, ConstantLiteralCondition);
        var forStatement = Assert.IsType<ForStatementSyntax>(pureBreakBody.Statements[0]);
        Assert.Null(pureBreakPlan.FindLoop(forStatement));

        var mixedBody = ParseBlock(
            "while (true) { if (b) { if (c) continue; Split(); break; } if (a) { Split(); break; } } Split();");
        var mixedPlan = CyclePlanner.Plan(mixedBody, HoldsSplit, ConstantLiteralCondition);
        var whileStatement = Assert.IsType<WhileStatementSyntax>(mixedBody.Statements[0]);
        var cycle = mixedPlan.FindLoop(whileStatement);
        Assert.NotNull(cycle);

        var loopBlock = Assert.IsType<BlockSyntax>(whileStatement.Statement);
        var continueIf = Assert.IsType<IfStatementSyntax>(loopBlock.Statements[0]);
        var continueThen = Assert.IsType<BlockSyntax>(continueIf.Statement);
        var breakIf = Assert.IsType<IfStatementSyntax>(loopBlock.Statements[1]);
        var breakThen = Assert.IsType<BlockSyntax>(breakIf.Statement);
        Assert.True(mixedPlan.IsGateStatement(cycle, continueThen.Statements[1]));
        Assert.False(mixedPlan.IsGateStatement(cycle, breakThen.Statements[0]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_StatementAfterContinueInsideTheLoop_IsNotGate()
    {
        var body = ParseBlock(
            "int k = 0; do { Split(); if (a) { if (c) continue; Split(); } k = 1; } while (k == 0); Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var doStatement = Assert.IsType<DoStatementSyntax>(body.Statements[1]);

        Assert.Null(plan.FindLoop(doStatement));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_LoopWithGateInSwitchOrNestedLoop_IsNotPlanned()
    {
        var switchGateBody = ParseBlock(
            "while (true) { Split(); if (a) { if (c) continue; Split(); } " +
            "switch (x) { case 0: if (d) continue; Split(); break; } break; } Split();");
        var switchGatePlan = CyclePlanner.Plan(switchGateBody, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(switchGatePlan.FindLoop(Assert.IsType<WhileStatementSyntax>(switchGateBody.Statements[0])));

        var nestedLoopGateBody = ParseBlock(
            "outer: while (true) { Split(); for (int i = 0; i < u; i++) { if (c) continue outer; Split(); break outer; } " +
            "break; } Split();");
        var nestedLoopGatePlan = CyclePlanner.Plan(nestedLoopGateBody, HoldsSplit, ConstantLiteralCondition);
        var outerLabeled = Assert.IsType<LabeledStatementSyntax>(nestedLoopGateBody.Statements[0]);
        Assert.Null(nestedLoopGatePlan.FindLoop(Assert.IsType<WhileStatementSyntax>(outerLabeled.Statement)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_LoopWithLabeledContinueAndElseGate_IsPlanned()
    {
        var body = ParseBlock(
            "outer: while (true) { Split(); if (a) { if (b) { for (int i = 0; i < u; i++) { if (c) continue outer; } } break; } " +
            "else { if (d) continue outer; Split(); break; } } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        var outerLabeled = Assert.IsType<LabeledStatementSyntax>(body.Statements[0]);
        var whileStatement = Assert.IsType<WhileStatementSyntax>(outerLabeled.Statement);
        var cycle = plan.FindLoop(whileStatement);

        Assert.NotNull(cycle);
        Assert.Equal(2, cycle.BackJumps.Length);
        var loopBlock = Assert.IsType<BlockSyntax>(whileStatement.Statement);
        var outerIf = Assert.IsType<IfStatementSyntax>(loopBlock.Statements[1]);
        Assert.NotNull(outerIf.Else);
        var elseBlock = Assert.IsType<BlockSyntax>(outerIf.Else.Statement);
        Assert.True(plan.IsGateStatement(cycle, elseBlock.Statements[1]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConstantFalseDoLoopWithContinue_IsNotPlanned()
    {
        var body = ParseBlock("do { Split(); if (c) continue; Split(); } while (false); Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(plan.FindLoop(Assert.IsType<DoStatementSyntax>(body.Statements[0])));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_LoopWithoutSplitPoint_IsNotPlanned()
    {
        var body = ParseBlock("while (true) { if (a) { if (c) continue; Plain(); } break; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(plan.FindLoop(Assert.IsType<WhileStatementSyntax>(body.Statements[0])));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_LoopWithForwardGotoPastGate_IsNotPlanned()
    {
        var body = ParseBlock(
            "while (true) { Split(); if (a) { if (b) goto PastGate; if (c) continue; Split(); PastGate: Split(); } break; } Split();");
        var plan = CyclePlanner.Plan(body, HoldsSplit, ConstantLiteralCondition);
        Assert.Null(plan.FindLoop(Assert.IsType<WhileStatementSyntax>(body.Statements[0])));
    }
}
