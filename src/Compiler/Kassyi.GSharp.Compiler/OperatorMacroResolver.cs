using Microsoft.CodeAnalysis.Operations;
using static Kassyi.GSharp.Compiler.OperatorMacroContext;
using static Kassyi.GSharp.Compiler.OperatorMacroOverloadResolver;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Resolves a <c>[GpuOperatorMacro]</c> operator: the target it maps to, where it is expanded, whether its target can
/// receive the result, and what evaluating its operands reads and writes. The analyzers, the C++ emitter
/// (<see cref="Transpiler.Rules.MacroRules.GpuOperatorMacroTranspiler"/>) and the managed expansion
/// (<see cref="Transpiler.ManagedOperatorMacroExpander"/>) read the same answer. The overload of the target is chosen by
/// <see cref="OperatorMacroOverloadResolver"/>, the context variable found by <see cref="OperatorMacroContext"/>.
/// </summary>
internal static class OperatorMacroResolver
{
    /// <summary>
    /// The call of an operator macro's target: the target, its context variable, and each operand (the result first) with
    /// the parameter it passes to.
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

    /// <summary>
    /// The call of the target of <paramref name="macro"/> with <paramref name="result"/> as its result (<see langword="null"/>:
    /// the temporary of an inner operator macro), or <paramref name="failure"/> (the message argument of GSH0003) when the
    /// context variable cannot be found.
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

        var (left, right, extra) = ExtractOperands(macro.OperatorCall);
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
    /// What evaluating each operand of <paramref name="call"/> reads and writes, in the C# order (result, left, right,
    /// extra): an operator macro among them as its operands and calls, an operand passed by reference as its location, any
    /// other as its value.
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

    // What computing <inner> reads and writes: its operands, and the calls of its target and of the operator macros nested
    // in it (ExpressionEffects.OfCall, with the context variable passed by reference)
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

    /// <summary>
    /// The <c>[GpuOperatorMacro]</c> operator that the right side of <paramref name="assignment"/> calls, and the overload
    /// of its target that the call maps to. The analyzer reports a target that cannot be resolved (GSH0003) from the same
    /// answer, and so are the forms an operator macro does not expand: a compound assignment whose operator or right
    /// side is an operator macro, an increment or decrement, and a lifted (nullable) operator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overload is chosen as C# overload resolution would choose it for a call with the operator's types (C# spec
    /// §12.6.4, Roslyn <c>OverloadResolution</c>), not by declaration order (#478): the result is the operator's return
    /// type and the operands are its parameter types. A candidate is a static, non-generic <c>[GpuDeviceFunction]</c>
    /// method of that name that takes the result and the operands, optionally after a context of a value type. A value
    /// parameter takes an implicit conversion; a <c>ref</c>, <c>out</c>, <c>in</c> or <c>ref readonly</c> parameter
    /// takes only its own type, and only the result may be <c>ref</c> or <c>out</c>. Of the candidates that apply, the
    /// one that is a better function member than every other one is chosen (§12.6.4.3): no argument converts better to
    /// the other one, and at least one converts better to it. A conversion is better when the argument's type is the
    /// parameter type and the other parameter type is not, otherwise when its parameter type is the better conversion
    /// target (§12.6.4.7: it converts implicitly to the other type and not back, or it is signed and the other unsigned).
    /// When the parameter types are the same, a value parameter is better than an <c>in</c> or <c>ref readonly</c> one.
    /// Without a better one, the call is ambiguous.
    /// </para>
    /// <para>
    /// [WHY] Whether a context is in scope does not take part in the choice: a local added to a kernel would otherwise
    /// change the function an operator calls. Two overloads that differ only by the context are ambiguous.
    /// </para>
    /// </remarks>
    public static bool TryResolve(AssignmentExpressionSyntax assignment, SemanticModel semanticModel, out OperatorMacro macro)
    {
        macro = default;
        var operatorCall = UnwrapExpression(assignment.Right, semanticModel);
        var (operatorMethod, isLifted, isIncrementOrDecrement) = GetOperator(operatorCall, semanticModel);
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            // c += a + b would become Add(c, a, b), which computes c = a + b; c += a has no call to become
            if (GetMacroAttribute(operatorMethod) == null &&
                GetMacroAttribute((semanticModel.GetSafeOperation(assignment) as ICompoundAssignmentOperation)?.OperatorMethod) == null)
            {
                return false;
            }

            macro = new OperatorMacro(operatorCall, null, false,
                $"The compound assignment '{assignment.OperatorToken.Text}' does not expand operator macros: give each operator macro a simple assignment of its own, as in 't = a + b; c = c + t;'");
            return true;
        }

        if (GetMacroAttribute(operatorMethod) is not { } macroAttr || operatorMethod == null)
        {
            return false;
        }

        macro = ResolveCall(operatorCall, operatorMethod, macroAttr, isLifted, isIncrementOrDecrement, semanticModel);
        if (macro.TargetMethod is not { } method)
        {
            return true;
        }

        var resultParameter = method.Parameters[macro.HasContext ? 1 : 0];
        if (!AcceptsTarget(assignment.Left, resultParameter, semanticModel))
        {
            var name = semanticModel.GetSymbol(assignment.Left) is { } symbol ? $"'{symbol.Name}' " : string.Empty;
            macro = new OperatorMacro(operatorCall, null, false,
                $"The target {name}of operator macro '{macro.TargetName}' does not convert to '{FormatType(resultParameter.Type)}' of parameter '{resultParameter.Name}'");
        }
        else if (IsPropertyWithoutStorage(assignment.Left, resultParameter, semanticModel))
        {
            macro = new OperatorMacro(operatorCall, null, false,
                $"The target '{assignment.Left}' of operator macro '{macro.TargetName}' is a property without storage: the target writes the result through its '{FormatRefKind(resultParameter.RefKind).TrimEnd()}' parameter '{resultParameter.Name}', which would reach a copy and never the setter; compute into a variable first and assign it, as in 't = a + b; {assignment.Left} = t;'");
        }

        return true;
    }

    /// <summary>
    /// Why the target of <paramref name="assignment"/> (the user's tree, an operator macro <paramref name="macro"/> with a
    /// target) cannot receive the result, or <see langword="null"/> when it can: a local or <c>out</c> parameter, or a
    /// field of a struct one, that is not definitely assigned before the assignment, when the target method takes the
    /// result as a value, <c>ref</c> or <c>in</c> parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [WHY] The expansion passes the target to the target method, which writes the result into the storage the target
    /// holds; it does not assign the target. An unassigned target has no storage: the C++ code reads an uninitialized
    /// value, and the managed expansion does not compile (CS0165, CS0170, CS0269). A target method that takes the result
    /// as <c>out</c> assigns it, so any target can receive it.
    /// </para>
    /// <para>
    /// Only what is certain is reported (semantics charter §3): a variable captured from outside the enclosing lambda or
    /// local function is assigned wherever the caller assigned it, which this body's data flow does not see.
    /// </para>
    /// </remarks>
    public static string? GetUnassignedTargetFailure(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        SemanticModel semanticModel)
    {
        if (macro.TargetMethod is not { } target ||
            target.Parameters[macro.HasContext ? 1 : 0].RefKind == RefKind.Out ||
            assignment.Parent is not ExpressionStatementSyntax statement ||
            GetTargetVariable(assignment.Left, semanticModel) is not { } symbol ||
            !IsUnassignedBefore(statement, symbol, semanticModel))
        {
            return null;
        }

        return
            $"The target '{assignment.Left}' of operator macro '{macro.TargetName}' is not assigned before the assignment: the target receives the result in the storage it holds, so assign '{symbol.Name}' first, as in '{symbol.Name} = default;'";
    }

    /// <summary>
    /// Why the context variable of <paramref name="assignment"/> (the user's tree), or of an operator macro nested in it,
    /// cannot be passed to its target, or <see langword="null"/> when it can: a local or <c>out</c> parameter that is not
    /// definitely assigned before the assignment.
    /// </summary>
    /// <remarks>
    /// [WHY] The expansion passes the variable itself (by reference, or as a copy of its value), which the target may
    /// read: the C++ code reads an uninitialized value, and the managed expansion does not compile (CS0165). Only what is
    /// certain is reported, as for the target (<see cref="GetUnassignedTargetFailure"/>).
    /// </remarks>
    public static string? GetUnassignedContextFailure(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        SemanticModel semanticModel)
    {
        if (assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return null;
        }

        if (macro is { HasContext: true, TargetMethod: { } target } &&
            TryFindContextVariable(assignment, target, semanticModel, out var variable, out _) &&
            variable is ILocalSymbol { IsRef: false } or IParameterSymbol { RefKind: RefKind.Out } &&
            IsUnassignedBefore(statement, variable, semanticModel))
        {
            return
                $"The context '{variable.Name}' of operator macro '{macro.TargetName}' is not assigned before the assignment: the target receives the context variable itself, so assign '{variable.Name}' first, as in '{variable.Name} = default;'";
        }

        if (macro.Nested.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var nested in macro.Nested)
        {
            if (GetUnassignedContextFailure(assignment, nested.Inner, semanticModel) is { } failure)
            {
                return failure;
            }
        }

        return null;
    }

    // Whether <symbol> (a local or out parameter of the method that holds <statement>) is certainly not assigned when
    // <statement> starts. A local declared with a value, and a variable captured from outside the enclosing lambda or
    // local function, are not
    private static bool IsUnassignedBefore(ExpressionStatementSyntax statement, ISymbol symbol,
        SemanticModel semanticModel)
    {
        if (IsAssignedWhereDeclared(symbol) ||
            !symbol.ContainingSymbol.IsEqualTo(semanticModel.GetEnclosingSymbol(statement.SpanStart)))
        {
            return false;
        }

        var flow = semanticModel.AnalyzeDataFlow(statement);
        return flow is { Succeeded: true } &&
               !flow.DefinitelyAssignedOnEntry
                   .AsStackEnumerable()
                   .Any(symbol, static (assigned, s) => assigned.IsEqualTo(s));
    }

    // The local or out parameter whose storage <target> is: the variable itself, or the variable that holds the struct
    // whose field it is (h.Inner.Value); null for anything else (an array element, a field of a class, a ref local)
    private static ISymbol? GetTargetVariable(ExpressionSyntax target, SemanticModel semanticModel)
    {
        while (true)
        {
            switch (semanticModel.GetSymbol(target))
            {
                case ILocalSymbol { IsRef: false } local:
                    return local;
                case IParameterSymbol { RefKind: RefKind.Out } parameter:
                    return parameter;
                case IFieldSymbol { IsStatic: false, ContainingType.IsValueType: true }
                    when target is MemberAccessExpressionSyntax { Expression: var receiver }:
                    target = receiver;
                    continue;
                default:
                    return null;
            }
        }
    }

    // A local declared with an initializer, or by a foreach statement, is assigned wherever it is in scope; the data
    // flow need not run for it
    private static bool IsAssignedWhereDeclared(ISymbol symbol) =>
        symbol is ILocalSymbol { DeclaringSyntaxReferences: [var reference] } &&
        reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null } or ForEachStatementSyntax;

    // The target that <operatorCall> (an operator macro <operatorMethod>) maps to, and the operator macros among its
    // operands, each computed into a temporary; or why it cannot be expanded
    private static OperatorMacro ResolveCall(ExpressionSyntax operatorCall, IMethodSymbol operatorMethod,
        AttributeData macroAttr, bool isLifted, bool isIncrementOrDecrement, SemanticModel semanticModel)
    {
        if (isIncrementOrDecrement || isLifted)
        {
            var op = GetOperatorText(operatorCall);
            return new OperatorMacro(operatorCall, null, false, isIncrementOrDecrement
                ? $"Operator macro '{op}' is an increment or decrement operator, which assigns its operand and is not expanded; write the assignment with a binary operator macro"
                : $"Operator macro '{op}' is applied to nullable operands (a lifted operator), which it does not expand; apply it to the values ('.Value')");
        }

        if (macroAttr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType ||
            macroAttr.ConstructorArguments[1].Value is not string targetMethodName)
        {
            return new OperatorMacro(operatorCall, null, false, null);
        }

        var target = $"{targetType.Name}.{targetMethodName}";
        if (ResolveOverload(targetType, targetMethodName, operatorMethod, semanticModel.Compilation, out var failure) is not
            var (method, hasContext))
        {
            return new OperatorMacro(operatorCall, null, false, $"Operator macro target '{target}' {failure}");
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
            var unwrapped = UnwrapExpression(operand, semanticModel);
            var (innerMethod, innerLifted, innerIncrementOrDecrement) = GetOperator(unwrapped, semanticModel);
            if (GetMacroAttribute(innerMethod) is not { } innerAttr || innerMethod == null)
            {
                continue;
            }

            var inner = ResolveCall(unwrapped, innerMethod, innerAttr, innerLifted, innerIncrementOrDecrement,
                semanticModel);

            // [WHY] The target writes its result into the storage of the assignment target: a temporary has storage
            // only when the result is a value passed by reference. The temporary is passed as it is: a conversion to
            // the outer operand type would be a call of its own
            var innerResult = inner.TargetMethod?.Parameters[inner.HasContext ? 1 : 0];
            var nestedFailure = innerResult switch
            {
                null => inner.Failure ?? $"Operator macro '{GetOperatorText(unwrapped)}' names no target method",
                { RefKind: not (RefKind.Ref or RefKind.Out) } => NoTemporary(unwrapped, inner, innerResult),
                _ when !innerMethod.ReturnType.IsUnmanagedType => NoTemporary(unwrapped, inner, innerResult),
                _ when !SymbolEqualityComparer.Default.Equals(innerMethod.ReturnType, operandType) =>
                    $"Operator macro '{GetOperatorText(unwrapped)}' returns '{FormatType(innerMethod.ReturnType)}', but the operand of '{GetOperatorText(operatorCall)}' takes '{FormatType(operandType)}'; assign it to a variable first, as in 't = a + b; c = c + t;'",
                _ => null
            };
            if (nestedFailure != null)
            {
                return new OperatorMacro(operatorCall, null, false, nestedFailure);
            }

            nested ??= ImmutableArray.CreateBuilder<NestedOperatorMacro>();
            nested.Add(new NestedOperatorMacro(operand, inner));
        }

        return new OperatorMacro(operatorCall, method, hasContext, null, target, operatorMethod.ReturnType,
            nested?.ToImmutable() ?? default);
    }

    private static string NoTemporary(ExpressionSyntax unwrapped, OperatorMacro inner, IParameterSymbol innerResult) =>
        $"Operator macro '{GetOperatorText(unwrapped)}' is an operand of another operator macro, but its target '{inner.TargetName}' takes the result as '{FormatRefKind(innerResult.RefKind)}{FormatType(innerResult.Type)} {innerResult.Name}', not as a ref or out parameter of an unmanaged type, so it has no temporary to compute into; assign it to a variable first, as in 't = a + b; c = c + t;'";

    // An operand (as written) that is an operator macro itself. [WHY] A class: the runtime refuses to load a struct that
    // holds a generic value type instantiated with the struct itself (ImmutableArray<(ExpressionSyntax, OperatorMacro)>
    // through ValueTuple<..., OperatorMacro>: TypeLoadException); ImmutableArray of a class loads
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
        /// Why the attribute's target cannot be called (the message argument of GSH0003), or <see langword="null"/> when
        /// it can, or when the attribute names no target.
        /// </summary>
        public string? Failure { get; } = failure;

        /// <summary>The target as the attribute names it (<c>Ops.Add</c>), for messages.</summary>
        public string TargetName { get; } = targetName;

        /// <summary>The operator's return type: the type of the temporary that an inner operator macro is computed into.</summary>
        public ITypeSymbol? ResultType { get; } = resultType;

        /// <summary>The operands (as written) that are operator macros themselves, computed into temporaries first.</summary>
        public ImmutableArray<NestedOperatorMacro> Nested { get; } = nested;

        /// <summary>The inner operator macro that <paramref name="operand"/> is, if any.</summary>
        public bool TryGetNested(ExpressionSyntax? operand, out OperatorMacro inner)
        {
            inner = default;
            if (operand == null || Nested.IsDefaultOrEmpty ||
                !Nested
                    .AsStackEnumerable()
                    .TryFirst(operand, static (candidate, o) => candidate.Operand == o, out var found))
            {
                return false;
            }

            inner = found.Inner;
            return true;
        }
    }

    /// <summary>
    /// Why the operator macro expression <paramref name="operatorExpression"/> is not expanded where it is, or
    /// <see langword="null"/> when it is: as the right side of an assignment statement (inside parentheses and casts), or
    /// as an operand of an operator macro that is. <see cref="TryResolve"/> reports what goes wrong there; anywhere else
    /// (a statement of its own such as <c>++x;</c>, an initializer, an argument) the operator would reach C++ as a
    /// struct operator that does not exist.
    /// </summary>
    public static string? GetPositionFailure(ExpressionSyntax operatorExpression, SemanticModel semanticModel)
    {
        var position = GetPosition(operatorExpression, semanticModel);
        if (position == Position.Expanded)
        {
            return null;
        }

        var op = operatorExpression is AssignmentExpressionSyntax assignment
            ? assignment.OperatorToken.Text
            : GetOperatorText(operatorExpression);
        return position == Position.UnderConversion
            ? $"Operator macro '{op}' is under a user-defined conversion, which its expansion would drop; assign it to a variable first, as in 't = a + b;', and convert the variable"
            : $"Operator macro '{op}' is expanded only as the right side of an assignment statement, or as an operand of an operator macro there; assign it to a variable first, as in 't = a + b;'";
    }

    private enum Position
    {
        Expanded,
        Elsewhere,

        // Inside a cast with a user-defined conversion, which the expansion would not apply (#759)
        UnderConversion
    }

    private static Position GetPosition(ExpressionSyntax operatorExpression, SemanticModel semanticModel)
    {
        // A compound assignment is the statement itself; TryResolve reports it
        if (operatorExpression is AssignmentExpressionSyntax)
        {
            return operatorExpression.Parent is ExpressionStatementSyntax ? Position.Expanded : Position.Elsewhere;
        }

        SyntaxNode node = operatorExpression;
        while (node.Parent is ParenthesizedExpressionSyntax ||
               (node.Parent is CastExpressionSyntax cast && IsTransparentCast(cast, semanticModel)))
        {
            node = node.Parent;
        }

        return node.Parent switch
        {
            CastExpressionSyntax => Position.UnderConversion,
            AssignmentExpressionSyntax assignment when assignment.Right == node =>
                assignment.Parent is ExpressionStatementSyntax ? Position.Expanded : Position.Elsewhere,
            ExpressionSyntax parent and (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax)
                when GetMacroAttribute(GetOperator(parent, semanticModel).Method) != null =>
                GetPosition(parent, semanticModel),
            _ => Position.Elsewhere
        };
    }

    // The operator call inside the parentheses and the transparent casts written around it: c = (T)(a + b) calls +
    // (#641). A cast with a user-defined conversion stops it: the expansion passes the operator's own result on, so
    // the conversion would be dropped (#759); GetPositionFailure reports the operator inside
    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast when IsTransparentCast(cast, semanticModel):
                    expression = cast.Expression;
                    break;
                default:
                    return expression;
            }
        }
    }

    // A cast whose conversion is not user-defined (an identity or built-in conversion), which the expansion can skip
    private static bool IsTransparentCast(CastExpressionSyntax cast, SemanticModel semanticModel) =>
        semanticModel.GetSafeOperation(cast) is not IConversionOperation { Conversion.IsUserDefined: true };

    // The user-defined operator that a binary, unary, increment or decrement expression calls, whether it is lifted to
    // nullable operands, and whether it is an increment or decrement
    private static (IMethodSymbol? Method, bool IsLifted, bool IsIncrementOrDecrement) GetOperator(
        ExpressionSyntax operatorCall, SemanticModel semanticModel)
    {
        if (operatorCall is not (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax))
        {
            return default;
        }

        var operation = semanticModel.GetSafeOperation(operatorCall);
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            IBinaryOperation binary => (binary.OperatorMethod, binary.IsLifted, false),
            IUnaryOperation unary => (unary.OperatorMethod, unary.IsLifted, false),
            IIncrementOrDecrementOperation incrementOrDecrement =>
                (incrementOrDecrement.OperatorMethod, incrementOrDecrement.IsLifted, true),
            _ => default
        };
    }

    // The [GpuOperatorMacro] of G# on <operatorMethod>, with its two arguments, or null
    private static AttributeData? GetMacroAttribute(IMethodSymbol? operatorMethod) =>
        operatorMethod?.GetAttribute(KnownAttributes.GpuOperatorMacro) is { } attribute &&
        attribute.AttributeClass?.ContainingNamespace.IsCoreNamespace() == true &&
        attribute.ConstructorArguments.Length >= 2
            ? attribute
            : null;

    // +, ++ (the operator as written)
    private static string GetOperatorText(ExpressionSyntax operatorCall) => operatorCall switch
    {
        BinaryExpressionSyntax binary => binary.OperatorToken.Text,
        PrefixUnaryExpressionSyntax prefix => prefix.OperatorToken.Text,
        PostfixUnaryExpressionSyntax postfix => postfix.OperatorToken.Text,
        _ => string.Empty
    };

    // Whether the assignment target passes to the result parameter of the chosen overload. The overload is chosen by the
    // operator's return type (decision V7), so a target of another type is checked here
    // A property that the target cannot write into by reference: one with accessors of its own (or an indexer) that does
    // not return by reference; neither has a backing field. An auto-property is a field on the device, and the managed path writes its backing field
    // (ManagedOperatorMacroExpander)
    private static bool IsPropertyWithoutStorage(ExpressionSyntax target, IParameterSymbol resultParameter,
        SemanticModel semanticModel) =>
        resultParameter.RefKind != RefKind.None &&
        semanticModel.GetSymbol(target) is IPropertySymbol { RefKind: RefKind.None } property &&
        !StructAbiMembers.IsAutoProperty(property);

    private static bool AcceptsTarget(ExpressionSyntax target, IParameterSymbol resultParameter, SemanticModel semanticModel)
    {
        if (semanticModel.GetTypeSymbol(target) is not { } targetType)
        {
            return true;
        }

        return resultParameter.RefKind == RefKind.None
            ? semanticModel.Compilation.ClassifyConversion(targetType, resultParameter.Type).IsImplicit
            : SymbolEqualityComparer.Default.Equals(targetType, resultParameter.Type);
    }

    internal static (ExpressionSyntax? Left, ExpressionSyntax? Right, ExpressionSyntax? Extra) ExtractOperands(
        ExpressionSyntax rightNode) => rightNode switch
        {
            BinaryExpressionSyntax { Left: var left, Right: var right } binary
                when binary.IsKind(SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression)
                => (left, null, right),
            BinaryExpressionSyntax { Left: var left, Right: var right }
                => (left, right, null),
            PrefixUnaryExpressionSyntax unary
                => (unary.Operand, null, null),
            _ => (null, null, null)
        };

    internal static bool IsPassedByReference(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnlyParameter;
}
