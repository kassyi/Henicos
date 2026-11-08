using static Kassyi.GSharp.Compiler.OperatorMacroContext;
using static Kassyi.GSharp.Compiler.OperatorMacroResolver;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Decides how the target of a resolved operator macro (<see cref="OperatorMacroResolver.TryResolve"/>) is called:
/// its context variable, its arguments in parameter order (how each is passed, the type it converts to, whether it is
/// evaluated into a temporary first), what evaluating the operands reads and writes, and how a <c>&amp;&amp;</c> or
/// <c>||</c> over it is expanded. The C++ emitter
/// (<see cref="Transpiler.Rules.MacroRules.GpuOperatorMacroTranspiler"/>) and the managed expansion
/// (<see cref="Transpiler.ManagedOperatorMacroExpander"/>) write the same plan.
/// </summary>
internal static class OperatorMacroCallPlanner
{
    /// <summary>
    /// The call of the target of <paramref name="macro"/> with <paramref name="result"/> as its result
    /// (<see langword="null"/>: the temporary of an inner operator macro), or <paramref name="failure"/> (the message
    /// argument of GSH0003) when the context variable cannot be found.
    /// </summary>
    public static bool TryPrepareCall(ExpressionSyntax? result, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, SemanticModel model, out MacroCall call, out string? failure)
    {
        call = default;
        failure = null;
        if (macro.TargetMethod is not { } target)
        {
            return false;
        }

        ISymbol? contextVariable = null;
        if (macro.HasContext && !TryFindContextVariable(assignment, target, model, out contextVariable, out failure))
        {
            return false;
        }

        // The result takes its parameter also when it is a temporary; each operand the operator has takes the next one
        var (left, right, extra) = OperatorMacroSyntax.ExtractOperands(macro.OperatorCall);
        var index = contextVariable != null ? 1 : 0;
        var resultOperand = new MacroOperand(result, target.Parameters[index++]);
        var leftOperand = MapOperand(left, target, ref index);
        var rightOperand = MapOperand(right, target, ref index);
        var extraOperand = MapOperand(extra, target, ref index);
        call = new MacroCall(target, contextVariable, resultOperand, leftOperand, rightOperand, extraOperand);
        return true;
    }

    // <operand> and the parameter at <index> (then the next one), or no parameter when the operator has no such operand
    private static MacroOperand MapOperand(ExpressionSyntax? operand, IMethodSymbol target, ref int index) =>
        operand != null ? new MacroOperand(operand, target.Parameters[index++]) : default;

    /// <summary>
    /// The arguments of <paramref name="call"/> in parameter order: the context, then the result and each operand the
    /// operator has. An operand up to <paramref name="lastSpilled"/> whose evaluation has an effect
    /// (<paramref name="effects"/>, from <see cref="GetOperandEffects"/>; <see langword="null"/>: none spills) is
    /// evaluated into a temporary first; an operator macro among the operands is always computed into one.
    /// </summary>
    internal static ImmutableArray<MacroArgument> PlanArguments(in MacroCall call, OperatorMacro macro,
        ExpressionEffects[]? effects, int lastSpilled, SemanticModel model)
    {
        var arguments = ImmutableArray.CreateBuilder<MacroArgument>(MacroCall.OperandRoles.Length + 1);
        if (call.ContextVariable != null)
        {
            arguments.Add(new MacroArgument(MacroArgumentKind.Context, call.Target.Parameters[0],
                MacroOperandRole.Context, null, null, null, spills: false));
        }

        foreach (var role in MacroCall.OperandRoles)
        {
            if (call.GetOperand(role) is not { Parameter: { } parameter } operand)
            {
                continue;
            }

            var index = (int)role;
            var spills = effects != null && index <= lastSpilled && !effects[index].IsEmpty;
            arguments.Add(PlanOperand(operand.Expression, parameter, role, macro, spills, model));
        }

        return arguments.ToImmutable();
    }

    // The argument of the operand in <role>: the temporary of the inner operator macro this call computes (no operand),
    // an inner operator macro, or the operand itself
    private static MacroArgument PlanOperand(ExpressionSyntax? operand, IParameterSymbol parameter,
        MacroOperandRole role, OperatorMacro macro, bool spills, SemanticModel model)
    {
        if (operand is null)
        {
            return new MacroArgument(MacroArgumentKind.InnerResult, parameter, role, null, null, null, spills: false);
        }

        if (macro.FindNested(operand) is { Inner.ResultType: not null } nested)
        {
            return new MacroArgument(MacroArgumentKind.NestedOperand, parameter, role, operand, nested, null,
                spills: false);
        }

        return new MacroArgument(MacroArgumentKind.Operand, parameter, role, operand, null,
            model.GetTypeSymbol(operand), spills);
    }

    /// <summary>
    /// What evaluating each operand of <paramref name="call"/> reads and writes, indexed by its
    /// <see cref="MacroOperandRole"/> (the C# order: result, left, right, extra): an operator macro among them as its
    /// operands and calls, an operand passed by reference as its location, any other as its value.
    /// </summary>
    internal static ExpressionEffects[] GetOperandEffects(in MacroCall call, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, SemanticModel model)
    {
        var effects = new ExpressionEffects[MacroCall.OperandRoles.Length];
        foreach (var role in MacroCall.OperandRoles)
        {
            if (call.GetOperand(role) is not { Expression: { } operand, Parameter: { } parameter })
            {
                continue;
            }

            effects[(int)role] = macro.TryGetNested(operand, out var inner) ? GetNestedEffects(inner, assignment, model)
                : IsPassedByReference(parameter) ? ExpressionEffects.OfLocation(operand, model)
                : ExpressionEffects.Of(operand, model);
        }

        return effects;
    }

    /// <summary>
    /// How a <c>&amp;&amp;</c> or <c>||</c> over an operator macro (<paramref name="macro"/>, with its
    /// <see cref="OperatorMacro.Test"/>) is expanded: <c>x &amp;&amp; y</c> is <c>T.false(x) ? x : T.&amp;(x, y)</c>
    /// and <c>x || y</c> is <c>T.true(x) ? x : T.|(x, y)</c> (C# spec §12.14.3), so the left operand is evaluated once
    /// into a temporary, tested, and either assigned to the result or passed to the target, whose call (with the right
    /// operand) runs only when the test fails.
    /// </summary>
    /// <remarks>
    /// C# evaluates the target of the assignment before the left operand. The target is evaluated into a reference
    /// first when it interferes with an operand (<paramref name="effects"/>, from <see cref="GetOperandEffects"/>):
    /// the left one is evaluated before the target is used, and C++ leaves the order of the call's arguments open.
    /// </remarks>
    internal static MacroConditionalPlan PlanConditional(in MacroCall call, OperatorMacro macro,
        ExpressionEffects[] effects)
    {
        var test = macro.Test ?? throw new InvalidOperationException("The operator macro is not a && or ||.");
        var target = effects[(int)MacroOperandRole.Result];
        var spillsTarget = call.Result.Expression != null && !target.IsEmpty &&
                           (target.InterferesWith(effects[(int)MacroOperandRole.Left]) ||
                            target.InterferesWith(effects[(int)MacroOperandRole.Right]));
        return new MacroConditionalPlan(test, IsPassedByReference(test.Parameters[0]), spillsTarget);
    }

    // What computing <inner> reads and writes: its operands, and the calls of its target and of the operator macros
    // nested in it (ExpressionEffects.OfCall, with the context variable passed by reference)
    private static ExpressionEffects GetNestedEffects(OperatorMacro inner, AssignmentExpressionSyntax assignment,
        SemanticModel model) =>
        AddCallEffects(inner, assignment, model, ExpressionEffects.Of(inner.OperatorCall, model));

    private static ExpressionEffects AddCallEffects(OperatorMacro macro, AssignmentExpressionSyntax assignment,
        SemanticModel model, ExpressionEffects effects)
    {
        if (macro.TargetMethod is { } target)
        {
            ISymbol? contextVariable = null;
            if (macro.HasContext)
            {
                TryFindContextVariable(assignment, target, model, out contextVariable, out _);
            }

            effects = effects.Union(ExpressionEffects.OfCall(target, contextVariable, model));
        }

        if (macro.Test is { } test)
        {
            effects = effects.Union(ExpressionEffects.OfCall(test, null, model));
        }

        if (macro.Nested.IsDefaultOrEmpty)
        {
            return effects;
        }

        foreach (var nested in macro.Nested)
        {
            effects = AddCallEffects(nested.Inner, assignment, model, effects);
        }

        return effects;
    }

    internal static bool IsPassedByReference(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnlyParameter;
}
