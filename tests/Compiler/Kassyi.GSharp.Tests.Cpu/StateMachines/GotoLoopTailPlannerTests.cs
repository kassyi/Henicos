#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for <see cref="GotoLoopTailPlanner"/>: a goto loop whose last statement is a block closes right after
/// the statement in the block (nested blocks included) that holds the last goto back to its label, unless something
/// else is open across that place or a goto jumps across it.
/// </summary>
[Category(TestCategory.Compiler)]
public class GotoLoopTailPlannerTests
{
    private static SyntaxList<StatementSyntax> ParseStatements(string code) =>
        ((BlockSyntax)SyntaxFactory.ParseStatement($"{{ {code} }}")).Statements;

    private static bool HoldsSplit(StatementSyntax s) =>
        s.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(static i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "Split" });

    // The close path of the goto loop of label in the statements of code
    private static int[] FindInnerClose(string code, string label)
    {
        var statements = ParseStatements(code);
        var plan = JumpRegionPlanner.Plan(statements, HoldsSplit);
        Assert.Empty(plan.Rejected);
        var labelIndex = statements
            .Select(static (statement, index) => (statement, index))
            .Single(entry => entry.statement is LabeledStatementSyntax labeled &&
                             labeled.Identifier.ValueText == label)
            .index;
        var loop = plan.Regions.Single(region =>
            region.Kind == JumpRegionKind.GotoLoop && region.LabelIndex == labelIndex);
        return GotoLoopTailPlanner.FindInnerClose(statements, plan, loop, HoldsSplit).ToArray();
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("top: Split(); { if (x) goto top; Split(); }", new[] { 0 })]
    [InlineData("top: Split(); { { if (x) goto top; Split(); } Split(); }", new[] { 0, 0 })]
    [InlineData("top: Split(); { if (x) goto top; Split(); if (y) goto top; Split(); }", new[] { 2 })]
    [InlineData("top: Split(); checked { if (x) goto top; Split(); }", new[] { 0 })]
    [InlineData("top: Split(); unsafe { if (x) goto top; Split(); }", new[] { 0 })]
    [InlineData("top: Split(); tail: { if (x) goto top; Split(); }", new[] { 0 })]
    [InlineData("top: Split(); { Split(); { if (x) goto top; } Split(); }", new[] { 1, 0 })]
    public void FindInnerClose_LastStatementIsBlock_ClosesAfterTheLastGotoToTheLoop(string code, int[] expected) =>
        Assert.Equal(expected, FindInnerClose(code, "top"));

    // The last statement is an if; a forward goto past the close inside the block, with or without a range of its own;
    // a goto from after the close back into the loop; a goto loop in the block open across the close
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("top: Split(); if (z) { if (x) goto top; Split(); }")]
    [InlineData("top: Split(); { if (y) goto skip; Split(); if (x) goto top; skip: Split(); }")]
    [InlineData("top: Split(); { if (y) goto skip; if (x) goto top; skip: ; Split(); }")]
    [InlineData("top: Split(); { mid: ; if (x) goto top; if (y) goto mid; Split(); }")]
    [InlineData("top: Split(); { again: { if (y) goto again; if (x) goto top; Split(); } Split(); }")]
    public void FindInnerClose_SomethingAcrossTheClose_ClosesAfterTheLastStatement(string code) =>
        Assert.Empty(FindInnerClose(code, "top"));

    // An inner goto loop ending at the same statement closes inside the block; the outer one closes after it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindInnerClose_TwoLoopsEndAtTheBlock_OnlyTheInnerClosesInside()
    {
        const string Code = "top: Split(); inner: Split(); { if (x) goto inner; Split(); if (y) goto top; }";

        Assert.Empty(FindInnerClose(Code, "top"));
        Assert.Equal(new[] { 0 }, FindInnerClose(Code, "inner"));
    }
}
