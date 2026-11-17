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
        Assert.Single(plan.Regions);
        Assert.Single(plan.Rejected);
        Assert.Equal("Mid", ((IdentifierNameSyntax)plan.Rejected[0].Expression!).Identifier.ValueText);
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
        Assert.Single(plan.Regions);
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
}
