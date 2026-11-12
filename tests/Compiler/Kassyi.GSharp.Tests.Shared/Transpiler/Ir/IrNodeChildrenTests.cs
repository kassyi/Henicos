#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the walks over the nodes of an IR tree on <c>(a + b) * c - d</c>, whose operand <c>a</c> is four levels
/// below the root.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrNodeChildrenTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DescendantsAndSelf_NestedOperators_ListsTheNodesInPreOrder() =>
        Assert.Equal(["(((a + b) * c) - d)", "((a + b) * c)", "(a + b)", "a", "b", "c", "d"],
            IrNodeChildren.DescendantsAndSelf(Nested()).Select(static node => IrPrinter.Print((IrExpression)node)));

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Exists_MatchOnlyAtTheDeepestLevel_IsFound() =>
        Assert.True(IrNodeChildren.Exists(Nested(), static node => IsVariable(node, "a")));

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Exists_NoMatch_IsNotFound() =>
        Assert.False(IrNodeChildren.Exists(Nested(), static node => IsVariable(node, "e")));

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Exists_SeveralMatches_StopsAtTheFirstInPreOrder()
    {
        var calls = 0;

        var found = IrNodeChildren.Exists(Nested(), node =>
        {
            calls++;
            return node is IrVariableReference;
        });

        // The three operators, then a; b, c and d are not looked at
        Assert.True(found);
        Assert.Equal(4, calls);
    }

    private static IrExpression Nested() => IrTestCases.Result(IrClassifierTests.Build("Nested"));

    private static bool IsVariable(IrNode node, string name) =>
        node is IrVariableReference reference && reference.Variable.Name == name;
}
