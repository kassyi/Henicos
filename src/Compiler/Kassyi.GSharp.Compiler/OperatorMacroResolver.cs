using Microsoft.CodeAnalysis.Operations;
using static Kassyi.GSharp.Compiler.OperatorMacroOverloadResolver;
using static Kassyi.GSharp.Compiler.OperatorMacroSyntax;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Resolves a <c>[GpuOperatorMacro]</c> operator: the target it maps to and the operator macros among its operands.
/// The analyzers, the C++ emitter (<see cref="Transpiler.Rules.MacroRules.GpuOperatorMacroTranspiler"/>) and the
/// managed expansion (<see cref="Transpiler.ManagedOperatorMacroExpander"/>) read the same answer. The overload of the
/// target is chosen by <see cref="OperatorMacroOverloadResolver"/>, the context variable found by
/// <see cref="OperatorMacroContext"/>; how the target is called is planned by <see cref="OperatorMacroCallPlanner"/>,
/// where the operator may be written is checked by <see cref="OperatorMacroPosition"/>, and whether its target and
/// context are assigned by <see cref="OperatorMacroAssignment"/>.
/// </summary>
internal static class OperatorMacroResolver
{
    /// <summary>
    /// The <c>[GpuOperatorMacro]</c> operator that the right side of <paramref name="assignment"/> calls, and the
    /// overload of its target that the call maps to. The analyzer reports a target that cannot be resolved (GSH0003)
    /// from the same answer, and so are the forms an operator macro does not expand: a compound assignment whose
    /// operator or right side is an operator macro, an increment or decrement, and a lifted (nullable) operator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overload is chosen as C# overload resolution would choose it for a call with the operator's types (C# spec
    /// §12.6.4, Roslyn <c>OverloadResolution</c>), not by declaration order (#478): the result is the operator's
    /// return type and the operands are its parameter types. A candidate is a static, non-generic
    /// <c>[GpuDeviceFunction]</c> method of that name that takes the result and the operands, optionally after a
    /// context of a value type. A value parameter takes an implicit conversion; a <c>ref</c>, <c>out</c>, <c>in</c> or
    /// <c>ref readonly</c> parameter takes only its own type, and only the result may be <c>ref</c> or <c>out</c>. Of
    /// the candidates that apply, the one that is a better function member than every other one is chosen
    /// (§12.6.4.3): no argument converts better to the other one, and at least one converts better to it. A conversion
    /// is better when the argument's type is the parameter type and the other parameter type is not, otherwise when its
    /// parameter type is the better conversion target (§12.6.4.7: it converts implicitly to the other type and not
    /// back, or it is signed and the other unsigned). When the parameter types are the same, a value parameter is
    /// better than an <c>in</c> or <c>ref readonly</c> one. Without a better one, the call is ambiguous.
    /// </para>
    /// <para>
    /// [WHY] Whether a context is in scope does not take part in the choice: a local added to a kernel would otherwise
    /// change the function an operator calls. Two overloads that differ only by the context are ambiguous.
    /// </para>
    /// </remarks>
    public static bool TryResolve(AssignmentExpressionSyntax assignment, SemanticModel semanticModel,
        out OperatorMacro macro)
    {
        macro = default;
        var operatorCall = UnwrapExpression(assignment.Right, semanticModel);
        var (operatorMethod, isLifted, isIncrementOrDecrement, test) = GetOperator(operatorCall, semanticModel);
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            // c += a + b would become Add(c, a, b), which computes c = a + b; c += a has no call to become
            var compound = semanticModel.GetSafeOperation(assignment) as ICompoundAssignmentOperation;
            if (GetMacroAttribute(operatorMethod) == null && GetMacroAttribute(compound?.OperatorMethod) == null)
            {
                return false;
            }

            macro = Failed(operatorCall, OperatorMacroMessages.CompoundAssignment(assignment.OperatorToken.Text));
            return true;
        }

        if (GetMacroAttribute(operatorMethod) is not { } macroAttr || operatorMethod == null)
        {
            return false;
        }

        macro = ResolveCall(operatorCall, operatorMethod, macroAttr, isLifted, isIncrementOrDecrement, test,
            semanticModel);
        if (macro.TargetMethod is { } method &&
            GetTargetFailure(assignment, macro, method, semanticModel) is { } failure)
        {
            macro = Failed(operatorCall, failure);
        }

        return true;
    }

    // Why the target of <assignment> cannot receive the result of <macro> (whose target is <method>), or null: it does
    // not pass to the result parameter, or it is a property that a by-reference result would never reach
    private static string? GetTargetFailure(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        IMethodSymbol method, SemanticModel semanticModel)
    {
        var resultParameter = method.Parameters[macro.HasContext ? 1 : 0];
        if (!AcceptsTarget(assignment.Left, resultParameter, semanticModel))
        {
            var name = semanticModel.GetSymbol(assignment.Left) is { } symbol ? $"'{symbol.Name}' " : string.Empty;
            return OperatorMacroMessages.TargetDoesNotConvert(name, macro.TargetName, resultParameter);
        }

        return IsPropertyWithoutStorage(assignment.Left, resultParameter, semanticModel)
            ? OperatorMacroMessages.PropertyWithoutStorage(assignment.Left, macro.TargetName, resultParameter)
            : null;
    }

    // The target that <operatorCall> (an operator macro <operatorMethod>, or a && or || over one that tests its left
    // operand with <test>) maps to, and the operator macros among its operands, each computed into a temporary; or why
    // it cannot be expanded
    private static OperatorMacro ResolveCall(ExpressionSyntax operatorCall, IMethodSymbol operatorMethod,
        AttributeData macroAttr, bool isLifted, bool isIncrementOrDecrement, IMethodSymbol? test,
        SemanticModel semanticModel)
    {
        if (isIncrementOrDecrement || isLifted)
        {
            var op = GetOperatorText(operatorCall);
            return Failed(operatorCall, isIncrementOrDecrement
                ? OperatorMacroMessages.IncrementOrDecrement(op)
                : OperatorMacroMessages.Lifted(op));
        }

        if (macroAttr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType ||
            macroAttr.ConstructorArguments[1].Value is not string targetMethodName)
        {
            return Failed(operatorCall, null);
        }

        var target = $"{targetType.Name}.{targetMethodName}";
        if (ResolveOverload(targetType, targetMethodName, operatorMethod, semanticModel.Compilation,
                out var failure) is not var (method, hasContext))
        {
            return Failed(operatorCall, OperatorMacroMessages.NoOverload(target, failure));
        }

        ImmutableArray<NestedOperatorMacro>.Builder? nested = null;
        var (left, right, extra) = ExtractOperands(operatorCall);
        ExpressionSyntax?[] operands = [left, right ?? extra];
        for (var operandIndex = 0; operandIndex < operands.Length; operandIndex++)
        {
            if (operands[operandIndex] is not { } operand)
            {
                continue;
            }

            var operandType = operatorMethod.Parameters[operandIndex].Type;
            if (!TryResolveNested(operand, operandType, operatorCall, semanticModel, out var inner,
                    out var nestedFailure))
            {
                return Failed(operatorCall, nestedFailure);
            }

            if (inner != null)
            {
                nested ??= ImmutableArray.CreateBuilder<NestedOperatorMacro>();
                nested.Add(inner);
            }
        }

        return new OperatorMacro(operatorCall, method, hasContext, null, target, operatorMethod.ReturnType,
            nested?.ToImmutable() ?? default) { Test = test };
    }

    // The operator macro that <operand> (an operand of <operatorCall>, of the operator's parameter type <operandType>)
    // is, to be computed into a temporary first. <nested> is null when the operand is no operator macro; false, with
    // <failure>, when it is one that cannot be computed into a temporary
    private static bool TryResolveNested(ExpressionSyntax operand, ITypeSymbol operandType,
        ExpressionSyntax operatorCall, SemanticModel semanticModel, out NestedOperatorMacro? nested,
        out string? failure)
    {
        nested = null;
        failure = null;
        var unwrapped = UnwrapExpression(operand, semanticModel);
        var (innerMethod, innerLifted, innerIncrement, innerTest) = GetOperator(unwrapped, semanticModel);
        if (GetMacroAttribute(innerMethod) is not { } innerAttr || innerMethod == null)
        {
            return true;
        }

        var inner = ResolveCall(unwrapped, innerMethod, innerAttr, innerLifted, innerIncrement, innerTest,
            semanticModel);
        failure = GetNestedFailure(unwrapped, inner, innerMethod.ReturnType, operandType, operatorCall);
        if (failure != null)
        {
            return false;
        }

        nested = new NestedOperatorMacro(operand, inner);
        return true;
    }

    // Why the inner operator macro <inner> (<unwrapped>, returning <innerType>) cannot be computed into a temporary
    // passed to the operand of <operatorCall> that takes <operandType>, or null when it can.
    // [WHY] The target writes its result into the storage of the assignment target: a temporary has storage only when
    // the result is a value passed by reference. The temporary is passed as it is: a conversion to the outer operand
    // type would be a call of its own
    private static string? GetNestedFailure(ExpressionSyntax unwrapped, OperatorMacro inner, ITypeSymbol innerType,
        ITypeSymbol operandType, ExpressionSyntax operatorCall)
    {
        var innerResult = inner.TargetMethod?.Parameters[inner.HasContext ? 1 : 0];
        var innerOp = GetOperatorText(unwrapped);
        return innerResult switch
        {
            null => inner.Failure ?? OperatorMacroMessages.NoTargetMethod(innerOp),
            { RefKind: not (RefKind.Ref or RefKind.Out) } =>
                OperatorMacroMessages.NoTemporary(innerOp, inner.TargetName, innerResult),
            _ when !innerType.IsUnmanagedType =>
                OperatorMacroMessages.NoTemporary(innerOp, inner.TargetName, innerResult),
            _ when !SymbolEqualityComparer.Default.Equals(innerType, operandType) =>
                OperatorMacroMessages.NestedTypeMismatch(innerOp, innerType, GetOperatorText(operatorCall),
                    operandType),
            _ => null
        };
    }

    // An operator macro that cannot be expanded, and why (null: the attribute names no target)
    private static OperatorMacro Failed(ExpressionSyntax operatorCall, string? failure) =>
        new(operatorCall, null, false, failure);

    // A property that the target cannot write into by reference: one with accessors of its own (or an indexer) that
    // does not return by reference; neither has a backing field. An auto-property is a field on the device, and the
    // managed path writes its backing field (ManagedOperatorMacroExpander)
    private static bool IsPropertyWithoutStorage(ExpressionSyntax target, IParameterSymbol resultParameter,
        SemanticModel semanticModel) =>
        resultParameter.RefKind != RefKind.None &&
        semanticModel.GetSymbol(target) is IPropertySymbol { RefKind: RefKind.None } property &&
        !StructAbiMembers.IsAutoProperty(property);

    // Whether the assignment target passes to the result parameter of the chosen overload. The overload is chosen by
    // the operator's return type (decision V7), so a target of another type is checked here
    private static bool AcceptsTarget(ExpressionSyntax target, IParameterSymbol resultParameter,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetTypeSymbol(target) is not { } targetType)
        {
            return true;
        }

        return resultParameter.RefKind == RefKind.None
            ? semanticModel.Compilation.ClassifyConversion(targetType, resultParameter.Type).IsImplicit
            : SymbolEqualityComparer.Default.Equals(targetType, resultParameter.Type);
    }

    // An operand (as written) that is an operator macro itself. [WHY] A class: the runtime refuses to load a struct
    // that holds a generic value type instantiated with the struct itself (ImmutableArray<(ExpressionSyntax,
    // OperatorMacro)> through ValueTuple<..., OperatorMacro>: TypeLoadException); ImmutableArray of a class loads
    internal sealed class NestedOperatorMacro(ExpressionSyntax operand, OperatorMacro inner)
    {
        public ExpressionSyntax Operand { get; } = operand;

        public OperatorMacro Inner { get; } = inner;
    }

    /// <summary>A call of a <c>[GpuOperatorMacro]</c> operator and the method it maps to.</summary>
    internal readonly struct OperatorMacro(
        ExpressionSyntax operatorCall,
        IMethodSymbol? targetMethod,
        bool hasContext,
        string? failure,
        string targetName = "",
        ITypeSymbol? resultType = null,
        ImmutableArray<NestedOperatorMacro> nested = default)
    {
        /// <summary>The operator expression, without the parentheses and casts around it.</summary>
        public ExpressionSyntax OperatorCall { get; } = operatorCall;

        /// <summary>The method the operator maps to, or <see langword="null"/> when it cannot be resolved.</summary>
        public IMethodSymbol? TargetMethod { get; } = targetMethod;

        /// <summary>Whether <see cref="TargetMethod"/> takes a context before the result.</summary>
        public bool HasContext { get; } = hasContext;

        /// <summary>
        /// Why the attribute's target cannot be called (the message argument of GSH0003), or <see langword="null"/>
        /// when it can, or when the attribute names no target.
        /// </summary>
        public string? Failure { get; } = failure;

        /// <summary>The target as the attribute names it (<c>Ops.Add</c>), for messages.</summary>
        public string TargetName { get; } = targetName;

        /// <summary>
        /// The operator's return type: the type of the temporary that an inner operator macro is computed into.
        /// </summary>
        public ITypeSymbol? ResultType { get; } = resultType;

        /// <summary>
        /// The operands (as written) that are operator macros themselves, computed into temporaries first.
        /// </summary>
        public ImmutableArray<NestedOperatorMacro> Nested { get; } = nested;

        /// <summary>
        /// The <c>operator false</c> (of <c>&amp;&amp;</c>) or <c>operator true</c> (of <c>||</c>) that the operator
        /// tests its left operand with before it calls <see cref="TargetMethod"/>, or <see langword="null"/> when the
        /// operator is not a conditional one (<see cref="OperatorMacroCallPlanner.PlanConditional"/>).
        /// </summary>
        public IMethodSymbol? Test { get; init; }

        /// <summary>The inner operator macro that <paramref name="operand"/> is, if any.</summary>
        public bool TryGetNested(ExpressionSyntax? operand, out OperatorMacro inner)
        {
            inner = default;
            if (FindNested(operand) is not { } found)
            {
                return false;
            }

            inner = found.Inner;
            return true;
        }

        /// <summary>The entry of <see cref="Nested"/> for <paramref name="operand"/>, if any.</summary>
        public NestedOperatorMacro? FindNested(ExpressionSyntax? operand)
        {
            if (operand == null || Nested.IsDefaultOrEmpty ||
                !Nested
                    .AsStackEnumerable()
                    .TryFirst(operand, static (candidate, o) => candidate.Operand == o, out var found))
            {
                return null;
            }

            return found;
        }
    }
}
