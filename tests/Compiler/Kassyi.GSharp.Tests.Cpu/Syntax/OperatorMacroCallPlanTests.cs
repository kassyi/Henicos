#nullable enable
using System.Collections.Immutable;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Kassyi.GSharp.Compiler.OperatorMacroCallPlanner;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The call plan of an operator macro (<see cref="OperatorMacroCallPlanner.PlanArguments"/>) that the C++ emitter and
/// the managed expansion both write: the arguments in parameter order, how each is passed, the type a value converts
/// to and whether it is evaluated into a temporary first, on the kernels of
/// <c>TestCases/Transpiler/OperatorMacroPlanCases.cs</c>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class OperatorMacroCallPlanTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/OperatorMacroPlanCases.cs")
    );

    // c = a + b: Add(ref c, a, b)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_BinaryOperator_PassesResultThenOperandsInParameterOrder()
    {
        var arguments = Plan("AddKernel");

        Assert.Equal(
            new[] { MacroOperandRole.Result, MacroOperandRole.Left, MacroOperandRole.Right },
            arguments.Select(static a => a.Role)
        );
        Assert.Equal(
            new[] { "c", "a", "b" },
            arguments.Select(static a => (a.Operand as IdentifierNameSyntax)?.Identifier.ValueText)
        );
        Assert.All(arguments, static a => Assert.Equal(MacroArgumentKind.Operand, a.Kind));
        Assert.Equal(new[] { true, false, false }, arguments.Select(static a => a.IsPassedByReference));
        Assert.All(arguments, static a => Assert.False(a.Spills));
    }

    // c = a - b: Sub(ctx, ref c, a, in b)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_TargetWithContext_PassesTheContextFirst()
    {
        var arguments = Plan("ContextKernel");

        Assert.Equal(MacroArgumentKind.Context, arguments[0].Kind);
        Assert.Equal(MacroOperandRole.Context, arguments[0].Role);
        Assert.Equal(new[] { 0, 1, 2, 3 }, arguments.Select(static a => a.Parameter.Ordinal));
        Assert.Equal(new[] { false, true, false, true }, arguments.Select(static a => a.IsPassedByReference));
    }

    // c = a % n: Widen(ref c, a, (long)n)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_OperandOfAnotherType_ConvertsToTheParameterType()
    {
        var arguments = Plan("ConversionKernel");

        Assert.Null(arguments[0].ConversionType);
        Assert.Null(arguments[1].ConversionType);
        Assert.Equal(SpecialType.System_Int16, arguments[2].OperandType?.SpecialType);
        Assert.Equal(SpecialType.System_Int64, arguments[2].ConversionType?.SpecialType);
    }

    // c = a << n: the count is the extra operand
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_Shift_PassesTheCountAsTheExtraOperand() =>
        Assert.Equal(
            new[] { MacroOperandRole.Result, MacroOperandRole.Left, MacroOperandRole.Extra },
            Plan("ShiftKernel").Select(static a => a.Role)
        );

    // c = a << n: Shift(ref c, a, n) takes the count as Extra; Right has neither an operand nor a parameter
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Prepare_Shift_NamesTheOperandsByRole()
    {
        var (assignment, model) = FindAssignment("ShiftKernel");
        Assert.True(OperatorMacroResolver.TryResolve(assignment, model, out var macro));
        Assert.True(TryPrepareCall(assignment.Left, macro, assignment, model, out var call, out _));

        Assert.Equal(new[] { "result", "value", "count" },
            new[] { call.Result, call.Left, call.Extra }.Select(static o => o.Parameter?.Name));
        Assert.Null(call.Right.Expression);
        Assert.Null(call.Right.Parameter);
        Assert.Equal("n", (call.Extra.Expression as IdentifierNameSyntax)?.Identifier.ValueText);
    }

    // c = (a + b) + d: the inner a + b is computed into a temporary of its own
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_OperatorMacroAsOperand_IsANestedOperand()
    {
        var arguments = Plan("NestedKernel");

        Assert.Equal(MacroArgumentKind.NestedOperand, arguments[1].Kind);
        Assert.IsType<ParenthesizedExpressionSyntax>(arguments[1].Operand);
        Assert.Equal(SyntaxKind.AddExpression, arguments[1].Nested?.Inner.OperatorCall.Kind());
        Assert.Equal(MacroArgumentKind.Operand, arguments[2].Kind);
    }

    // The inner call computes its result into the temporary of the outer one: the result has no operand
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_InnerOperatorMacro_PassesTheResultTemporary()
    {
        var (assignment, model) = FindAssignment("NestedKernel");
        Assert.True(OperatorMacroResolver.TryResolve(assignment, model, out var macro));
        var inner = Assert.Single(macro.Nested).Inner;
        Assert.True(TryPrepareCall(null, inner, assignment, model, out var call, out _));

        var arguments = PlanArguments(call, inner, null, -1, model);

        Assert.Equal(MacroArgumentKind.InnerResult, arguments[0].Kind);
        Assert.Null(arguments[0].Operand);
    }

    // values[Ops.Take(ref k)] = values[k] * 2: the target writes k, which the left operand reads, so both are evaluated
    // into temporaries in the C# order; the constant after them stays in place
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_OperandsUpToTheLastThatInterferes_Spill()
    {
        var arguments = Plan("SpillKernel");

        Assert.Equal(new[] { true, true, false }, arguments.Select(static a => a.Spills));
    }

    // The plan with the spills the C++ emitter computes (EvaluationOrder.LastSpilledOperand)
    private static ImmutableArray<MacroArgument> Plan(string kernel)
    {
        var (assignment, model) = FindAssignment(kernel);
        Assert.True(OperatorMacroResolver.TryResolve(assignment, model, out var macro));
        Assert.Null(macro.Failure);
        Assert.True(TryPrepareCall(assignment.Left, macro, assignment, model, out var call, out _));
        var effects = GetOperandEffects(call, macro, assignment, model);
        return PlanArguments(call, macro, effects, EvaluationOrder.LastSpilledOperand(effects), model);
    }

    // The last assignment of <kernel>
    private static (AssignmentExpressionSyntax Assignment, SemanticModel Model) FindAssignment(string kernel)
    {
        var tree = s_compilation.SyntaxTrees.Last();
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == kernel);
        var assignment = method.DescendantNodes().OfType<AssignmentExpressionSyntax>().Last();
        return (assignment, s_compilation.GetSemanticModel(tree));
    }
}
