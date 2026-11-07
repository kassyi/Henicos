using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The expansion plan of <c>&amp;&amp;</c> and <c>||</c> over operator macros
/// (<see cref="OperatorMacroResolver.PlanConditional"/>), on the kernels of <see cref="OperatorMacroConditionalKernels"/>:
/// which truth operator tests the left operand, and whether the target is evaluated before it.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class OperatorMacroConditionalPlanTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("AndKernel", "op_False")]
    [InlineData("OrKernel", "op_True")]
    public void Plan_ConditionalOperator_TestsWithTheTruthOperatorOfItsKind(string kernel, string test)
    {
        var plan = PlanFirstAssignment(kernel);

        Assert.Equal(test, plan.Test.Name);
        Assert.False(plan.PassesTestByReference);
        Assert.False(plan.SpillsTarget);
    }

    // values[2 * i + index[i]] reads index, which the left operand writes
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_TargetThatTheLeftOperandChanges_IsEvaluatedFirst()
    {
        var plan = PlanFirstAssignment("TargetOrderKernel");

        Assert.Equal("op_False", plan.Test.Name);
        Assert.True(plan.SpillsTarget);
    }

    // e = (a && Count(...)) | two: the outer | is no conditional operator, its left operand is one
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Resolve_ConditionalOperatorAsOperand_IsANestedConditionalOperatorMacro()
    {
        var (assignment, model) = FindAssignments("NestedKernel").Last();

        Assert.True(OperatorMacroResolver.TryResolve(assignment, model, out var macro));
        Assert.Null(macro.Test);
        var nested = Assert.Single(macro.Nested);
        Assert.Equal("op_False", nested.Inner.Test?.Name);
    }

    private static OperatorMacroResolver.ConditionalPlan PlanFirstAssignment(string kernel)
    {
        var (assignment, model) = FindAssignments(kernel).First();
        Assert.True(OperatorMacroResolver.TryResolve(assignment, model, out var macro));
        Assert.True(OperatorMacroResolver.TryPrepareCall(assignment.Left, macro, assignment, model, out var call, out _));
        var effects = OperatorMacroResolver.GetOperandEffects(call, macro, assignment, model);
        return OperatorMacroResolver.PlanConditional(call, macro, effects);
    }

    // The assignments of <kernel> whose right side is an operator, in source order
    private static (AssignmentExpressionSyntax Assignment, SemanticModel Model)[] FindAssignments(string kernel,
        [CallerFilePath] string callerFilePath = "")
    {
        var kernelFile = Path.Combine(Path.GetDirectoryName(callerFilePath) ?? "", "OperatorMacroConditionalKernels.cu.cs");
        var (_, tree, model) = RoslynTestUtility.GetKernelContext(kernelFile);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == kernel);
        return method.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Right is BinaryExpressionSyntax)
            .Select(a => (a, model))
            .ToArray();
    }
}
