#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies which jumps <see cref="UsingExitPlan"/> finds leaving the body of a CPU <c>using</c>, how they share exits,
/// and what it decides for a value kept by a <c>return</c>, without writing C++.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class UsingExitPlanTests
{
    private static readonly (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) s_context =
        RoslynTestUtility.GetTestCaseContext("Transpiler/UsingExitPlanCases.cs");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ReturnLeavesNestedUsings_OuterDeclaresTheKeptValue()
    {
        var outer = Plan("Nested", 0);
        var inner = Plan("Nested", 1);

        Assert.Equal([UsingExitKind.Return], outer.Exits.Select(static e => e.Kind));
        Assert.Equal([UsingExitKind.Return], inner.Exits.Select(static e => e.Kind));
        Assert.True(outer.DeclaresReturnValue);
        Assert.False(inner.DeclaresReturnValue);
        Assert.Equal(SpecialType.System_Int32, outer.ReturnType?.SpecialType);
        Assert.False(outer.ReturnsByRef);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_GotoCase_LeavesOnlyForTheSwitchAroundTheUsing()
    {
        var plan = Plan("GotoCase", 0);

        var exit = Assert.Single(plan.Exits);
        Assert.Equal(UsingExitKind.GotoCase, exit.Kind);
        Assert.True(IsGotoCase(exit.Jump, 2));
        var inner = Jumps("GotoCase").First(static j => IsGotoCase(j, 1));
        Assert.Equal(-1, plan.FindExit(inner));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_ReturnsOfLambdaAndLocalFunction_StayInTheBody()
    {
        var plan = Plan("Functions", 0);

        Assert.Empty(plan.Exits);
        Assert.False(plan.ReturnsValue);
        Assert.All(Jumps("Functions"), jump => Assert.Equal(-1, plan.FindExit(jump)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_TwoGotosToOneLabel_ShareOneExit()
    {
        var plan = Plan("SameLabel", 0);

        var exit = Assert.Single(plan.Exits);
        Assert.Equal(UsingExitKind.Goto, exit.Kind);
        Assert.All(Jumps("SameLabel"), jump => Assert.Equal(0, plan.FindExit(jump)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_BreakAndContinueOfTheOuterLoop_LeaveButTheInnerBreakStays()
    {
        var plan = Plan("Loops", 0);

        Assert.Equal([UsingExitKind.Continue, UsingExitKind.Break], plan.Exits.Select(static e => e.Kind));
        var innerBreak = Jumps("Loops").First();
        Assert.Equal(-1, plan.FindExit(innerBreak));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ReturnByReference_KeepsAPointer()
    {
        var plan = Plan("ByRef", 0);

        Assert.True(plan.ReturnsByRef);
        Assert.True(plan.DeclaresReturnValue);
        Assert.Equal(SpecialType.System_Int32, plan.ReturnType?.SpecialType);
    }

    private static MethodDeclarationSyntax Method(string name) =>
        s_context.Tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == name);

    // goto case <label>; with an integer label
    private static bool IsGotoCase(SyntaxNode jump, int label) =>
        jump is GotoStatementSyntax
        {
            RawKind: (int)SyntaxKind.GotoCaseStatement,
            Expression: LiteralExpressionSyntax { Token.Value: int value }
        } && value == label;

    private static StatementSyntax[] Jumps(string method) =>
        Method(method)
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(static s => s is ReturnStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax
                or GotoStatementSyntax)
            .ToArray();

    // The plan of the index-th using statement or declaration of the method, in source order
    private static UsingExitPlan Plan(string method, int index)
    {
        var owner = Method(method)
            .DescendantNodes()
            .Where(static n => n is UsingStatementSyntax or LocalDeclarationStatementSyntax
            {
                UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword
            })
            .ElementAt(index);
        if (owner is UsingStatementSyntax statement)
        {
            return UsingExitPlan.Create(statement, statement.Statement, statement.Statement, s_context.Model, null);
        }

        var block = (BlockSyntax)owner.Parent!; // A using declaration is a statement of a block
        var position = block.Statements.IndexOf((StatementSyntax)owner);
        var first = position + 1 < block.Statements.Count ? block.Statements[position + 1] : null;
        return UsingExitPlan.Create(owner, first, block.Statements.Last(), s_context.Model, null);
    }
}
