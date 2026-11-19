#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies that <see cref="IrExpression.KeepSpelling{T}"/> and <see cref="IrExpression.WithDecision{TNode}"/> carry
/// decisions across node updates and copies (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrDecisionCarryTests
{
    private const string TestCase = "Ir/IrDecisionCases.cs";
    private const string TypeName = "IrDecisionCases.Numeric";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Update_WithChangedChildren_CarriesTheDecision()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, "MixedUnsigned");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));

        var assignment = (IrAssignment)((IrExpressionStatement)IrTestCases.Statement(decided, 0)).Expression;
        var binary = (IrBinary)IrExpression.WithParentheses(assignment.Value, 2);
        var updated = binary.Update(
            IrExpression.WithParentheses(binary.Left, 1),
            IrExpression.WithParentheses(binary.Right, 1));

        Assert.NotSame(binary, updated);
        Assert.Same(binary.Decision, updated.Decision);
        Assert.Equal(2, updated.Parentheses);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void WithDecision_ReturnsACopy_AndKeepsTheOriginalUndecided()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, "MixedUnsigned");
        var assignment = (IrAssignment)((IrExpressionStatement)IrTestCases.Statement(built, 0)).Expression;
        var binary = (IrBinary)assignment.Value;
        var decision = new IrBinaryDecision(null, null, null, null, 0, null,
            compilation.GetSpecialType(SpecialType.System_Int64), true, true, null);

        var withDecision = binary.WithDecision(decision);
        Assert.NotSame(binary, withDecision);
        Assert.Null(binary.Decision);
        Assert.Same(decision, withDecision.Decision);

        var sameAgain = withDecision.WithDecision(decision);
        Assert.Same(withDecision, sameAgain);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_Twice_RebuildsEveryDecision()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, "MixedUnsigned");
        var context = IrDecisionTestContext.Create(compilation, built);

        var first = IrDecisions.Decide(built, context);
        var second = IrDecisions.Decide(first, context);

        var firstBinary = ((IrAssignment)((IrExpressionStatement)IrTestCases.Statement(first, 0)).Expression).Value;
        var secondBinary = ((IrAssignment)((IrExpressionStatement)IrTestCases.Statement(second, 0)).Expression).Value;

        Assert.NotSame(firstBinary.Decision, secondBinary.Decision);
        var d1 = ((IrBinary)firstBinary).Decided;
        var d2 = ((IrBinary)secondBinary).Decided;
        Assert.Equal(d1.PromotedType, d2.PromotedType, SymbolEqualityComparer.Default);
        Assert.Equal(d1.ConvertsLeft, d2.ConvertsLeft);
        Assert.Equal(d1.ConvertsRight, d2.ConvertsRight);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rebuild_OfTheWholeCorpusCase_KeepsEveryDecision()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var rewriter = new ReparenthesizeEveryExpression();

        foreach (var built in IrTestCases.BuildAll(TestCase, TypeName))
        {
            var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
            var rebuilt = rewriter.RewriteMethod(decided);

            Assert.Empty(IrDecisionInvariants.Verify(rebuilt.Body));

            var originalExpressions = IrNodeChildren.DescendantsAndSelf(decided.Body).OfType<IrExpression>().ToArray();
            var rebuiltExpressions = IrNodeChildren.DescendantsAndSelf(rebuilt.Body).OfType<IrExpression>().ToArray();
            Assert.Equal(originalExpressions.Length, rebuiltExpressions.Length);

            for (var i = 0; i < originalExpressions.Length; i++)
            {
                Assert.Same(originalExpressions[i].Decision, rebuiltExpressions[i].Decision);
            }
        }
    }

    private sealed class ReparenthesizeEveryExpression : IrRewriter
    {
        public override IrExpression VisitExpression(IrExpression expression)
        {
            var visited = base.VisitExpression(expression);
            return IrExpression.WithParentheses(visited, visited.Parentheses + 1);
        }
    }
}
