#nullable enable
using System;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

[Category(TestCategory.Compiler)]
public class JumpRegionPlannerTests
{
    private static SyntaxList<StatementSyntax> ParseStatements(string code) =>
        ((BlockSyntax)SyntaxFactory.ParseStatement($"{{ {code} }}")).Statements;

    private static bool HoldsSplit(StatementSyntax s) =>
        s.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(static i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "Split" });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_EmptyStatements_ReturnsEmpty()
    {
        var statements = ParseStatements("");
        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Same(JumpRegionPlan.Empty, plan);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NoLabels_ReturnsEmpty()
    {
        var statements = ParseStatements("int a = 1; Split(); int b = 2;");
        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Same(JumpRegionPlan.Empty, plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_SingleForwardGoto_ProducesForwardRange()
    {
        var statements = ParseStatements(@"
            int a = 1;
            if (a > 0) goto L;
            Split();
        L:
            int c = 3;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Regions);
        Assert.Empty(plan.Rejected);

        var r = plan.Regions[0];
        Assert.Equal(JumpRegionKind.ForwardRange, r.Kind);
        Assert.Equal(1, r.First);
        Assert.Equal(2, r.Last);
        Assert.Equal(3, r.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_TwoGotosToSameLabel_ProducesSingleRangeWithEarliestFirst()
    {
        var statements = ParseStatements(@"
            if (x) goto L;
            Split();
            if (y) goto L;
        L:
            int c = 3;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Regions);
        Assert.Empty(plan.Rejected);

        var r = plan.Regions[0];
        Assert.Equal(JumpRegionKind.ForwardRange, r.Kind);
        Assert.Equal(0, r.First);
        Assert.Equal(2, r.Last);
        Assert.Equal(3, r.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_NestedForwardGotos_ProducesNestedRanges()
    {
        var statements = ParseStatements(@"
            if (x) goto Outer;
            Split();
            if (y) goto Inner;
            Split();
        Inner:
            int mid = 1;
        Outer:
            int end = 2;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Empty(plan.Rejected);

        var outer = plan.Regions[0];
        Assert.Equal(0, outer.First);
        Assert.Equal(4, outer.Last);
        Assert.Equal(5, outer.LabelIndex);

        var inner = plan.Regions[1];
        Assert.Equal(2, inner.First);
        Assert.Equal(3, inner.Last);
        Assert.Equal(4, inner.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_CrossingForwardGotos_ExpandsOuterRangeFirst()
    {
        var statements = ParseStatements(@"
            if (x) goto fail;
            Split();
            if (y) goto done;
        fail:
            Split();
            int b = 1;
        done:
            int c = 2;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Empty(plan.Rejected);

        var doneRange = plan.Regions[0];
        Assert.Equal(0, doneRange.First);
        Assert.Equal(4, doneRange.Last);
        Assert.Equal(5, doneRange.LabelIndex);

        var failRange = plan.Regions[1];
        Assert.Equal(0, failRange.First);
        Assert.Equal(2, failRange.Last);
        Assert.Equal(3, failRange.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_RangeWithoutSplitPoints_IsDiscarded()
    {
        var statements = ParseStatements(@"
            if (x) goto L;
            int a = 1;
        L:
            int b = 2;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Same(JumpRegionPlan.Empty, plan);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SyncGridNextLabel_IsExcluded()
    {
        var statements = ParseStatements(@"
            if (x) goto __gsharp_sg_next_0;
            Split();
        __gsharp_sg_next_0:
            int b = 2;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Same(JumpRegionPlan.Empty, plan);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_BackwardGotoIntoMiddleOfRange_IsRejected()
    {
        var statements = ParseStatements(@"
            if (x) goto End;
            Split();
        Mid:
            Split();
        End:
            int a = 1;
            goto Mid;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Equal(2, plan.Rejected.Length);
        Assert.Contains(plan.Rejected,
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "Mid" });
        Assert.Contains(plan.Rejected,
            static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "End" });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_BackwardGotoToRangeStart_IsNotRejected()
    {
        var statements = ParseStatements(@"
        Start:
            if (x) goto End;
            Split();
        End:
            int a = 1;
            goto Start;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Empty(plan.Rejected);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoIntoRangeMiddle_AdoptsItsRangeInsideTheOuterOne()
    {
        // goto M has no split point before M, but M lies inside the range of L that starts after the goto
        var statements = ParseStatements(@"
            if (a) goto M;
            if (b) goto L;
        M:
            Split();
        L:
            int c = 3;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Equal((0, 3), (plan.Regions[0].First, plan.Regions[0].LabelIndex));
        Assert.Equal((0, 2), (plan.Regions[1].First, plan.Regions[1].LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_SingleBackwardGoto_ProducesGotoLoop()
    {
        var statements = ParseStatements(@"
        L:
            Split();
            if (x) goto L;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Regions);
        Assert.Empty(plan.Rejected);

        var r = plan.Regions[0];
        Assert.Equal(JumpRegionKind.GotoLoop, r.Kind);
        Assert.Equal(0, r.First);
        Assert.Equal(1, r.Last);
        Assert.Equal(0, r.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_BackwardGotoInLabeledStatementItself_ProducesGotoLoop()
    {
        var statements = ParseStatements(@"
        L:
            if (x) { Split(); goto L; }");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Regions);
        Assert.Empty(plan.Rejected);

        var r = plan.Regions[0];
        Assert.Equal(JumpRegionKind.GotoLoop, r.Kind);
        Assert.Equal(0, r.First);
        Assert.Equal(0, r.Last);
        Assert.Equal(0, r.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_BackwardGotoWithoutSplitPoints_IsDiscarded()
    {
        var statements = ParseStatements(@"
        L:
            int a = 1;
            if (x) goto L;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Same(JumpRegionPlan.Empty, plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_TwoBackwardGotosToSameLabel_ExtendsLastToLatestGoto()
    {
        var statements = ParseStatements(@"
        L:
            int a = 0;
            if (x) goto L;
            Split();
            if (y) goto L;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Regions);
        Assert.Empty(plan.Rejected);

        var r = plan.Regions[0];
        Assert.Equal(JumpRegionKind.GotoLoop, r.Kind);
        Assert.Equal(0, r.First);
        Assert.Equal(3, r.Last);
        Assert.Equal(0, r.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToGotoLoopStartWithoutSplit_AdoptsForwardRange()
    {
        var statements = ParseStatements(@"
            if (a) goto L;
            int b = 1;
        L:
            Split();
            if (x) goto L;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);

        Assert.Equal(JumpRegionKind.ForwardRange, plan.Regions[0].Kind);
        Assert.Equal((0, 1, 2), (plan.Regions[0].First, plan.Regions[0].Last, plan.Regions[0].LabelIndex));

        Assert.Equal(JumpRegionKind.GotoLoop, plan.Regions[1].Kind);
        Assert.Equal((2, 3, 2), (plan.Regions[1].First, plan.Regions[1].Last, plan.Regions[1].LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ForwardGotoExitingGotoLoop_ExpandsForwardRangeToLoopStart()
    {
        var statements = ParseStatements(@"
        L:
            Split();
            if (x) goto After;
            if (y) goto L;
        After:
            int z = 0;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);

        Assert.Equal(JumpRegionKind.ForwardRange, plan.Regions[0].Kind);
        Assert.Equal((0, 2, 3), (plan.Regions[0].First, plan.Regions[0].Last, plan.Regions[0].LabelIndex));

        Assert.Equal(JumpRegionKind.GotoLoop, plan.Regions[1].Kind);
        Assert.Equal((0, 2, 0), (plan.Regions[1].First, plan.Regions[1].Last, plan.Regions[1].LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoAndGotoLoopWithSameSpan_OrdersForwardRangeOuter()
    {
        var statements = ParseStatements(@"
        L:
            if (x) goto After;
            Split();
            if (y) goto L;
        After:
            int z = 0;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);

        Assert.Equal(JumpRegionKind.ForwardRange, plan.Regions[0].Kind);
        Assert.Equal((0, 2, 3), (plan.Regions[0].First, plan.Regions[0].Last, plan.Regions[0].LabelIndex));

        Assert.Equal(JumpRegionKind.GotoLoop, plan.Regions[1].Kind);
        Assert.Equal((0, 2, 0), (plan.Regions[1].First, plan.Regions[1].Last, plan.Regions[1].LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_BackwardGotoFromOutsideIntoGotoLoopMiddle_IsRejected()
    {
        var statements = ParseStatements(@"
        L:
            int a = 0;
        Mid:
            Split();
            if (x) goto L;
            int b = 1;
            if (y) goto Mid;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Rejected);
        Assert.Equal("Mid", Assert.IsType<IdentifierNameSyntax>(plan.Rejected[0].Expression).Identifier.ValueText);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_ForwardGotoIntoGotoLoopMiddle_IsRejected()
    {
        var statements = ParseStatements(@"
            if (a) goto Mid;
        L:
            int b = 0;
        Mid:
            Split();
            if (x) goto L;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        var region = Assert.Single(plan.Regions);
        Assert.Equal(JumpRegionKind.GotoLoop, region.Kind);
        Assert.Single(plan.Rejected);
        Assert.Equal("Mid", Assert.IsType<IdentifierNameSyntax>(plan.Rejected[0].Expression).Identifier.ValueText);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoToGotoLoopStart_IsNotRejected()
    {
        var statements = ParseStatements(@"
            if (a) goto L;
        L:
            Split();
            if (x) goto L;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_CrossingGotoLoops_IsRejected()
    {
        var statements = ParseStatements(@"
        A:
            Split();
        B:
            Split();
            if (x) goto A;
            if (y) goto B;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Rejected);
        Assert.Equal("B", Assert.IsType<IdentifierNameSyntax>(plan.Rejected[0].Expression).Identifier.ValueText);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_DoublyLabeledGotoLoop_TargetsSecondLabelAtSameIndex()
    {
        var statements = ParseStatements(@"
        first:
        second:
            Split();
            if (x) goto second;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Single(plan.Regions);
        Assert.Empty(plan.Rejected);

        var r = plan.Regions[0];
        Assert.Equal(JumpRegionKind.GotoLoop, r.Kind);
        Assert.Equal(0, r.First);
        Assert.Equal(1, r.Last);
        Assert.Equal(0, r.LabelIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ForwardGotoWithOwnSplitExitingGotoLoop_ExpandsFirstToLoopStart()
    {
        var statements = ParseStatements(@"
        L:
            int a = 0;
            if (x) goto After;
            Split();
            if (y) goto L;
        After:
            int z = 0;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Equal(JumpRegionKind.ForwardRange, plan.Regions[0].Kind);
        Assert.Equal((0, 3, 4), (plan.Regions[0].First, plan.Regions[0].Last, plan.Regions[0].LabelIndex));
        Assert.Equal(JumpRegionKind.GotoLoop, plan.Regions[1].Kind);
        Assert.Equal((0, 3, 0), (plan.Regions[1].First, plan.Regions[1].Last, plan.Regions[1].LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConsecutiveEmptyLabeledStatements_MapsBothLabelsToGroupHead()
    {
        var statements = ParseStatements(@"
            if (a) goto L1;
        L0:
            ;
        L1:
            Split();
            if (x) goto L0;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        Assert.Equal(2, plan.Regions.Length);
        Assert.Equal(JumpRegionKind.ForwardRange, plan.Regions[0].Kind);
        Assert.Equal((0, 0, 1), (plan.Regions[0].First, plan.Regions[0].Last, plan.Regions[0].LabelIndex));
        Assert.Equal(JumpRegionKind.GotoLoop, plan.Regions[1].Kind);
        Assert.Equal((1, 3, 1), (plan.Regions[1].First, plan.Regions[1].Last, plan.Regions[1].LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConsecutiveEmptyLabeledStatementsDoubleLoop_MergesIntoSingleGotoLoop()
    {
        var statements = ParseStatements(@"
        L0:
            ;
        L1:
            ;
            Split();
            if (x) goto L0;
            if (y) goto L1;");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        var region = Assert.Single(plan.Regions);
        Assert.Equal(JumpRegionKind.GotoLoop, region.Kind);
        Assert.Equal((0, 4, 0), (region.First, region.Last, region.LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ConsecutiveEmptyLabeledStatementsForwardRange_TargetsSecondLabel()
    {
        var statements = ParseStatements(@"
            if (a) goto L1;
            Split();
        L0:
            ;
        L1:
            Split();");

        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        var region = Assert.Single(plan.Regions);
        Assert.Equal(JumpRegionKind.ForwardRange, region.Kind);
        Assert.Equal((0, 1, 2), (region.First, region.Last, region.LabelIndex));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FindCrossSectionRejectedGotos_GotoToGotoLoopHead_IsRejected()
    {
        var switchStatement = (SwitchStatementSyntax)SyntaxFactory.ParseStatement(@"
            switch (u)
            {
                case 0:
                    if (a) goto top;
                    if (d) goto selfLoop;
                    break;
                case 1:
                    if (b) goto top;
                top:
                    Split();
                    if (c) goto top;
                    break;
                case 2:
                selfLoop:
                    if (e) { Split(); goto selfLoop; }
                    break;
            }");

        var plans = PlanSections(switchStatement);
        var rejected = JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, plans);
        Assert.Equal(2, rejected.Length);
        Assert.Equal(("top", "top"), (rejected[0].TargetLabelName, rejected[0].RegionLabelName));
        Assert.Equal(("selfLoop", "selfLoop"), (rejected[1].TargetLabelName, rejected[1].RegionLabelName));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FindCrossSectionRejectedGotos_GotoToForwardRangeHead_IsAllowed()
    {
        var switchStatement = (SwitchStatementSyntax)SyntaxFactory.ParseStatement(@"
            switch (u)
            {
                case 0:
                    if (a) goto start;
                    break;
                case 1:
                start:
                    if (b) goto end;
                    Split();
                end:
                    break;
            }");

        var plans = PlanSections(switchStatement);
        var rejected = JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, plans);
        Assert.Empty(rejected);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FindCrossSectionRejectedGotos_GotoToForwardRangeMiddle_IsRejected()
    {
        var switchStatement = (SwitchStatementSyntax)SyntaxFactory.ParseStatement(@"
            switch (u)
            {
                case 0:
                    if (a) goto mid;
                    break;
                case 1:
                    if (b) goto end;
                    Split();
                mid:
                    Split();
                end:
                    break;
            }");

        var plans = PlanSections(switchStatement);
        var rejected = JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, plans);
        var item = Assert.Single(rejected);
        Assert.Equal(("mid", "end"), (item.TargetLabelName, item.RegionLabelName));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FindCrossSectionRejectedGotos_ChainedLabelToGotoLoopHead_IsRejected()
    {
        var switchStatement = (SwitchStatementSyntax)SyntaxFactory.ParseStatement(@"
            switch (u)
            {
                case 0:
                    if (a) goto second;
                    break;
                case 1:
                first:
                    ;
                second:
                    Split();
                    if (b) goto first;
                    break;
            }");

        var plans = PlanSections(switchStatement);
        var rejected = JumpRegionPlanner.FindCrossSectionRejectedGotos(switchStatement, plans);
        var item = Assert.Single(rejected);
        Assert.Equal(("second", "first"), (item.TargetLabelName, item.RegionLabelName));
    }

    private static JumpRegionPlan[] PlanSections(SwitchStatementSyntax switchStatement)
    {
        var plans = new JumpRegionPlan[switchStatement.Sections.Count];
        for (var i = 0; i < switchStatement.Sections.Count; i++)
        {
            plans[i] = JumpRegionPlanner.Plan(switchStatement.Sections[i].Statements, HoldsSplit);
        }

        return plans;
    }
}
