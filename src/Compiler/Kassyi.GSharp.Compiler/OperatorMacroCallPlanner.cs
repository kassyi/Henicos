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
    /// The call of an operator macro's target: the target, its context variable, and each operand (the result first)
    /// with the parameter it passes to.
    /// </summary>
    internal readonly struct MacroCall(IMethodSymbol target, ISymbol? contextVariable, ExpressionSyntax?[] operands,
        IParameterSymbol?[] parameters)
    {
        public IMethodSymbol Target { get; } = target;

        public ISymbol? ContextVariable { get; } = contextVariable;

        // [result, left, right, extra]: the result is null when it is the temporary of an inner operator macro
        public ExpressionSyntax?[] Operands { get; } = operands;

        public IParameterSymbol?[] Parameters { get; } = parameters;
    }

    /// <summary>What an argument of the target's call is.</summary>
    internal enum MacroArgumentKind
    {
        /// <summary>The context variable (<see cref="MacroCall.ContextVariable"/>).</summary>
        Context,

        /// <summary>An operand as written, or the result when it is the assignment target.</summary>
        Operand,

        /// <summary>An operand that is an operator macro itself, computed into a temporary of its own first.</summary>
        NestedOperand,

        /// <summary>The result of an inner operator macro: the temporary its outer call computes it into.</summary>
        InnerResult
    }

    /// <summary>One argument of the target's call (<see cref="PlanArguments"/>).</summary>
    internal readonly struct MacroArgument(
        MacroArgumentKind kind,
        IParameterSymbol parameter,
        int slot,
        ExpressionSyntax? operand,
        NestedOperatorMacro? nested,
        ITypeSymbol? operandType,
        bool spills)
    {
        public MacroArgumentKind Kind { get; } = kind;

        /// <summary>The parameter of the target that the argument passes to.</summary>
        public IParameterSymbol Parameter { get; } = parameter;

        /// <summary>
        /// The index of the operand in <see cref="MacroCall.Operands"/> (0 the result, 1 left, 2 right, 3 extra), or -1
        /// for the context.
        /// </summary>
        public int Slot { get; } = slot;

        /// <summary>The operand as written; <see langword="null"/> for the context and an inner result.</summary>
        public ExpressionSyntax? Operand { get; } = operand;

        /// <summary>The operator macro that a <see cref="MacroArgumentKind.NestedOperand"/> is.</summary>
        public NestedOperatorMacro? Nested { get; } = nested;

        /// <summary>The type of <see cref="Operand"/>, when it has one.</summary>
        public ITypeSymbol? OperandType { get; } = operandType;

        /// <summary>
        /// The parameter type when the operand's type is another one: the value converts to it, implicitly in C++ and
        /// through a cast on the managed path; <see langword="null"/> when no conversion applies.
        /// </summary>
        public ITypeSymbol? ConversionType =>
            OperandType is { } type && !type.IsEqualTo(Parameter.Type) ? Parameter.Type : null;

        /// <summary>Whether the parameter takes the argument by reference.</summary>
        public bool IsPassedByReference => OperatorMacroCallPlanner.IsPassedByReference(Parameter);

        /// <summary>
        /// Whether the operand is evaluated into a temporary before the call, to keep the C# order of evaluation
        /// (<see cref="EvaluationOrder.LastSpilledOperand"/>).
        /// </summary>
        public bool Spills { get; } = spills;
    }

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

        var (left, right, extra) = OperatorMacroSyntax.ExtractOperands(macro.OperatorCall);
        ExpressionSyntax?[] operands = [result, left, right, extra];
        call = new MacroCall(target, contextVariable, operands,
            MapParameters(operands, target, contextVariable != null ? 1 : 0));
        return true;
    }

    // The parameter each operand passes to (null: the operator has no such operand). Slot 0 is the result, which takes
    // its parameter also when it is a temporary
    private static IParameterSymbol?[] MapParameters(ExpressionSyntax?[] operands, IMethodSymbol target, int first)
    {
        var parameters = new IParameterSymbol?[operands.Length];
        parameters[0] = target.Parameters[first];
        var index = first + 1;
        for (var i = 1; i < operands.Length; i++)
        {
            parameters[i] = operands[i] != null ? target.Parameters[index++] : null;
        }

        return parameters;
    }

    /// <summary>
    /// The arguments of <paramref name="call"/> in parameter order: the context, then the result and each operand the
    /// operator has. An operand up to <paramref name="lastSpilled"/> whose evaluation has an effect
    /// (<paramref name="effects"/>, from <see cref="GetOperandEffects"/>; <see langword="null"/>: none spills) is
    /// evaluated into a temporary first; an operator macro among the operands is always computed into one.
    /// </summary>
    internal static ImmutableArray<MacroArgument> PlanArguments(in MacroCall call, OperatorMacro macro,
        ExpressionEffects[]? effects, int lastSpilled, SemanticModel model)
    {
        var arguments = ImmutableArray.CreateBuilder<MacroArgument>(call.Operands.Length + 1);
        if (call.ContextVariable != null)
        {
            arguments.Add(new MacroArgument(MacroArgumentKind.Context, call.Target.Parameters[0], -1, null, null, null,
                spills: false));
        }

        for (var i = 0; i < call.Operands.Length; i++)
        {
            if (call.Parameters[i] is not { } parameter)
            {
                continue;
            }

            arguments.Add(PlanOperand(call.Operands[i], parameter, i, macro,
                effects != null && i <= lastSpilled && !effects[i].IsEmpty, model));
        }

        return arguments.ToImmutable();
    }

    // The argument of the operand in <slot>: the temporary of the inner operator macro this call computes (no operand),
    // an inner operator macro, or the operand itself
    private static MacroArgument PlanOperand(ExpressionSyntax? operand, IParameterSymbol parameter, int slot,
        OperatorMacro macro, bool spills, SemanticModel model)
    {
        if (operand is null)
        {
            return new MacroArgument(MacroArgumentKind.InnerResult, parameter, slot, null, null, null, spills: false);
        }

        if (macro.FindNested(operand) is { Inner.ResultType: not null } nested)
        {
            return new MacroArgument(MacroArgumentKind.NestedOperand, parameter, slot, operand, nested, null,
                spills: false);
        }

        return new MacroArgument(MacroArgumentKind.Operand, parameter, slot, operand, null,
            model.GetTypeSymbol(operand), spills);
    }

    /// <summary>
    /// What evaluating each operand of <paramref name="call"/> reads and writes, in the C# order (result, left, right,
    /// extra): an operator macro among them as its operands and calls, an operand passed by reference as its location,
    /// any other as its value.
    /// </summary>
    internal static ExpressionEffects[] GetOperandEffects(in MacroCall call, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, SemanticModel model)
    {
        var effects = new ExpressionEffects[call.Operands.Length];
        for (var i = 0; i < effects.Length; i++)
        {
            if (call.Operands[i] is not { } operand || call.Parameters[i] is not { } parameter)
            {
                continue;
            }

            effects[i] = macro.TryGetNested(operand, out var inner) ? GetNestedEffects(inner, assignment, model)
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
    internal static ConditionalPlan PlanConditional(in MacroCall call, OperatorMacro macro, ExpressionEffects[] effects)
    {
        var test = macro.Test ?? throw new InvalidOperationException("The operator macro is not a && or ||.");
        var spillsTarget = call.Operands[0] != null && !effects[0].IsEmpty &&
                           (effects[0].InterferesWith(effects[1]) || effects[0].InterferesWith(effects[2]));
        return new ConditionalPlan(test, IsPassedByReference(test.Parameters[0]), spillsTarget);
    }

    /// <summary>
    /// The expansion of a <c>&amp;&amp;</c> or <c>||</c> over an operator macro (<see cref="PlanConditional"/>).
    /// </summary>
    internal readonly struct ConditionalPlan(IMethodSymbol test, bool passesTestByReference, bool spillsTarget)
    {
        /// <summary>The <c>operator false</c> or <c>operator true</c> that tests the left operand.</summary>
        public IMethodSymbol Test { get; } = test;

        /// <summary>Whether the test takes the left operand by reference (<c>in</c>).</summary>
        public bool PassesTestByReference { get; } = passesTestByReference;

        /// <summary>Whether the target is evaluated into a reference before the left operand.</summary>
        public bool SpillsTarget { get; } = spillsTarget;
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
