#nullable enable
using System;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

[Category(TestCategory.Compiler)]
public class SwitchJoinPlannerTests
{
    private static SwitchStatementSyntax ParseSwitch(string code) =>
        (SwitchStatementSyntax)SyntaxFactory.ParseStatement(code);

    private static SwitchJoinPlan Plan(SwitchStatementSyntax switchStatement) =>
        SwitchJoinPlanner.Plan(
            switchStatement,
            SwitchCaseLookup.CreateSectionLookup(switchStatement, semanticModel: null));

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_NoCrossSectionJumps_ReturnsEmpty()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    if (c) goto local;
                local:
                    break;
                case 1:
                    break;
            }");

        var plan = Plan(sw);
        Assert.Same(SwitchJoinPlan.Empty, plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_GotoCaseToLaterSection_ProducesSectionHeadJoin()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    a();
                    goto case 1;
                case 1:
                    b();
                    break;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        var join = Assert.Single(plan.Joins);
        Assert.Equal(new SwitchJoin(Order: 1, Section: 1, Position: 0), join);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[0]);
        Assert.Equal(new[] { 1, 2 }, plan.FirstHeld[1]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_CrossSectionGotoToLabel_ProducesLabelJoin()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    a();
                    goto shared;
                case 1:
                    b();
                shared:
                    c();
                    break;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        var join = Assert.Single(plan.Joins);
        Assert.Equal(new SwitchJoin(Order: 1, Section: 1, Position: 2), join);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[0]);
        Assert.Equal(new[] { 1, 2 }, plan.FirstHeld[1]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_ChainedGotoCase_OrdersJoinsTopologicallyAndComputesFirstHeld()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    goto case 1;
                case 1:
                    goto case 2;
                case 2:
                    break;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        Assert.Equal(
            new[]
            {
                new SwitchJoin(Order: 1, Section: 1, Position: 0),
                new SwitchJoin(Order: 2, Section: 2, Position: 0)
            },
            plan.Joins);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[0]);
        Assert.Equal(new[] { 1, 2 }, plan.FirstHeld[1]);
        Assert.Equal(new[] { 2, 3 }, plan.FirstHeld[2]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_TwoSourcesToSameSection_ProducesSingleJoin()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    goto case 1;
                case 1:
                    break;
                case 2:
                    goto case 1;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        var join = Assert.Single(plan.Joins);
        Assert.Equal(new SwitchJoin(Order: 1, Section: 1, Position: 0), join);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[0]);
        Assert.Equal(new[] { 1, 2 }, plan.FirstHeld[1]);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[2]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_GotoCaseToEarlierSection_OrdersTargetBeforeSource()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    break;
                case 1:
                    goto case 0;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        var join = Assert.Single(plan.Joins);
        Assert.Equal(new SwitchJoin(Order: 1, Section: 0, Position: 0), join);
        Assert.Equal(new[] { 1, 2 }, plan.FirstHeld[0]);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[1]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_GotoCaseToOwnSection_PlansNoJoin()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    a();
                    if (c) goto case 0;
                    break;
                case 1:
                    break;
            }");

        Assert.Same(SwitchJoinPlan.Empty, Plan(sw));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_GotoCaseToOwnSectionAlsoEnteredFromAnother_RejectsTheLoopBack()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    a();
                    if (c) goto case 0;
                    break;
                case 1:
                    goto case 0;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Joins);
        var rejected = Assert.Single(plan.Rejected);
        Assert.Same(sw.Sections[0], rejected.FirstAncestorOrSelf<SwitchSectionSyntax>());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_GotoCaseToOwnSectionAfterItsJoin_RejectsTheLoopBack()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    goto shared;
                case 1:
                    a();
                shared:
                    b();
                    if (c) goto case 1;
                    break;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Joins);
        var rejected = Assert.Single(plan.Rejected);
        Assert.True(rejected.IsKind(SyntaxKind.GotoCaseStatement));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_CyclicGotoCase_RejectsCyclicJumpsWithNoJoins()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    goto case 1;
                case 1:
                    goto case 0;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Joins);
        Assert.Empty(plan.FirstHeld);
        Assert.Equal(2, plan.Rejected.Length);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_SkippedJoinDiamond_ComputesFirstHeldPastSkippedJoin()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    if (c) goto case 1;
                    goto case 2;
                case 1:
                    break;
                case 2:
                    break;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        Assert.Equal(
            new[]
            {
                new SwitchJoin(Order: 1, Section: 1, Position: 0),
                new SwitchJoin(Order: 2, Section: 2, Position: 0)
            },
            plan.Joins);
        Assert.Equal(new[] { 1 }, plan.FirstHeld[0]);
        Assert.Equal(new[] { 1, 3 }, plan.FirstHeld[1]);
        Assert.Equal(new[] { 2, 3 }, plan.FirstHeld[2]);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_BackwardGotoInSectionAcrossJoin_RejectsItWithNoJoins()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    goto first;
                case 1:
                first:
                    a();
                second:
                    b();
                    if (c) goto first;
                    break;
                default:
                    goto second;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Joins);
        Assert.Empty(plan.FirstHeld);
        var rejected = Assert.Single(plan.Rejected);
        Assert.Same(sw.Sections[1], rejected.FirstAncestorOrSelf<SwitchSectionSyntax>());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_BackwardGotoInSectionWithinOneSegment_KeepsTheJoin()
    {
        var sw = ParseSwitch(@"
            switch (x)
            {
                case 0:
                    goto first;
                case 1:
                first:
                    a();
                    if (c) goto first;
                    break;
            }");

        var plan = Plan(sw);
        Assert.Empty(plan.Rejected);
        Assert.Equal(new SwitchJoin(Order: 1, Section: 1, Position: 1), Assert.Single(plan.Joins));
    }
}
