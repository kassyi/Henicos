#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrDecisions.Decide(IrStateMachine, IrDecisionContext)"/> after split lowering and
/// state-machine construction (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrDecideAfterSplitTests
{
    private const string LinearCases = "Ir/IrLinearCases.cs";
    private const string LinearCasesType = "IrLinear.IrLinearCases";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_StateMachine_KeepsTheShapeAndDecidesEveryNode()
    {
        var compilation = IrTestCases.Compile(LinearCases);
        var (built, machine) = BuildMachine(compilation, "WhileBarrier");
        var decided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));

        Assert.Equal(machine.States.Length, decided.States.Length);
        for (var i = 0; i < machine.States.Length; i++)
        {
            Assert.Equal(machine.States[i].Statements.Length, decided.States[i].Statements.Length);
        }

        Assert.Equal(
            IrNodeChildren.DescendantsAndSelf(machine).Length,
            IrNodeChildren.DescendantsAndSelf(decided).Length);
        Assert.Empty(IrDecisionInvariants.Verify(decided));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ConditionOfAWhile_IsDecided()
    {
        var compilation = IrTestCases.Compile(LinearCases);
        var (built, machine) = BuildMachine(compilation, "WhileBarrier");
        var decided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));

        var conditionalJump = decided.States
            .SelectMany(static s => s.Statements)
            .OfType<IrStateJump>()
            .First(static j => j.Condition is not null);
        var comparison = Assert.IsType<IrBinary>(conditionalJump.Condition);
        Assert.NotNull(comparison.Decision);
        Assert.Null(comparison.Decided.Helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_AfterThePass_DoesNotTouchTheInput()
    {
        var compilation = IrTestCases.Compile(LinearCases);
        var (built, machine) = BuildMachine(compilation, "WhileBarrier");
        var context = IrDecisionTestContext.Create(compilation, machine, built);

        var first = IrDecisions.Decide(machine, context);
        var second = IrDecisions.Decide(machine, context);

        Assert.NotSame(first, second);
        foreach (var expression in IrNodeChildren.DescendantsAndSelf(machine).OfType<IrExpression>())
        {
            Assert.Null(expression.Decision);
        }

        var firstExpressions = IrNodeChildren.DescendantsAndSelf(first).OfType<IrExpression>().ToArray();
        var secondExpressions = IrNodeChildren.DescendantsAndSelf(second).OfType<IrExpression>().ToArray();
        Assert.Equal(firstExpressions.Length, secondExpressions.Length);
        for (var i = 0; i < firstExpressions.Length; i++)
        {
            if (firstExpressions[i].Decision is not null || secondExpressions[i].Decision is not null)
            {
                Assert.NotSame(firstExpressions[i].Decision, secondExpressions[i].Decision);
            }
        }
    }

    private static (IrMethod Built, IrStateMachine Machine) BuildMachine(Compilation compilation, string methodName) =>
        IrDecisionTestContext.BuildSplitMachine(compilation, LinearCases, LinearCasesType, methodName);
}
