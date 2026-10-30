using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Intercepts and transpiles expressions decorated with <c>[GpuOperatorMacro]</c> into specialized device function calls.
/// </summary>
internal static class GpuOperatorMacroTranspiler
{
    // The result of comparing two candidates, or two conversions of the same argument (Roslyn's BetterResult)
    private const int Neither = 0;
    private const int Left = 1;
    private const int Right = 2;

    /// <summary>
    /// Attempts to intercept and transpile an assignment expression decorated with a GPU operator macro.
    /// </summary>
    public static bool TryTranspile(AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        if (!TryResolve(assignment, context.Query.SemanticModel, out var macro))
        {
            return false;
        }

        if (macro.TargetMethod == null)
        {
            if (macro.Failure is { } targetFailure)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, targetFailure);
            }

            return false;
        }

        if (!macro.Nested.IsDefaultOrEmpty)
        {
            // An operator macro among the operands is computed into a temporary first: a block of statements
            using (context.Emitter.Writer.Scope())
            {
                return AppendExpansion(assignment.Left, -1, macro, assignment, ref context);
            }
        }

        if (!TryPrepareCall(assignment.Left, macro, assignment, ref context, out var call))
        {
            return false;
        }

        var effects = GetOperandEffects(call, macro, assignment, context.Query.SemanticModel);
        var last = EvaluationOrder.LastSpilledOperand(effects);

        // C# evaluates the target and then the operands from left to right; when they interfere, they are evaluated
        // into temporaries in that order first (EvaluationOrder)
        int[]? temps = null;
        using (var sequence = last >= 0 ? EvaluationOrder.BeginSequence(context.Emitter.Writer) : default)
        {
            if (last >= 0)
            {
                // Without an operator macro among the operands, declaring cannot fail
                _ = DeclareOperandTemps(call, macro, effects, last, assignment, ref context, out temps);
                sequence.Return();
            }

            AppendCall(call, temps, -1, assignment, ref context);
        }

        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // { auto __gsharp_seq_0 = <operand>; <T> __gsharp_seq_1{}; <inner target>(&__gsharp_seq_1, <inner operands>); <target>(<result>, __gsharp_seq_0, __gsharp_seq_1); }
    // The call of <macro> with <result> (or the temporary <resultTemp>) as the result, after its operands: each operator
    // macro among them is computed into a temporary of its own, and the operands before the last one that interferes
    // with a later one (an inner call included) are evaluated into temporaries, in the C# order (EvaluationOrder)
    private static bool AppendExpansion(ExpressionSyntax? result, int resultTemp, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        if (!TryPrepareCall(result, macro, assignment, ref context, out var call))
        {
            return false;
        }

        var effects = GetOperandEffects(call, macro, assignment, context.Query.SemanticModel);
        if (!DeclareOperandTemps(call, macro, effects, EvaluationOrder.LastSpilledOperand(effects), assignment,
                ref context, out var temps))
        {
            return false;
        }

        AppendCall(call, temps, resultTemp, assignment, ref context);
        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // The call of an operator macro's target: the target, its context variable, and each operand (the result first) with
    // the parameter it passes to
    private readonly struct MacroCall(IMethodSymbol target, ISymbol? contextVariable, ExpressionSyntax?[] operands,
        IParameterSymbol?[] parameters)
    {
        public IMethodSymbol Target { get; } = target;

        public ISymbol? ContextVariable { get; } = contextVariable;

        // [result, left, right, extra]: the result is null when it is the temporary of an inner operator macro
        public ExpressionSyntax?[] Operands { get; } = operands;

        public IParameterSymbol?[] Parameters { get; } = parameters;
    }

    private static bool TryPrepareCall(ExpressionSyntax? result, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, ref TranspileContext context, out MacroCall call)
    {
        call = default;
        if (macro.TargetMethod is not { } target)
        {
            return false;
        }

        ISymbol? contextVariable = null;
        if (macro.HasContext &&
            !TryFindContextVariable(assignment, target, context.Query.SemanticModel, out contextVariable,
                out var failure))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, failure);
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

    // What evaluating each operand reads and writes: an operator macro among them as its operands and calls
    // (GetNestedEffects), an operand passed by reference as its location, any other as its value
    private static ExpressionEffects[] GetOperandEffects(in MacroCall call, OperatorMacro macro,
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

    // In the order of the operands: an operator macro among them as <T> __gsharp_seq_<n>{}; and its own expansion, which
    // computes it into that temporary; an operand up to <last> that has an effect as auto&& __gsharp_seq_<n> = <operand
    // passed by reference>; or auto __gsharp_seq_<n> = <operand>; (-1 in <temps>: emitted in place)
    private static bool DeclareOperandTemps(in MacroCall call, OperatorMacro macro, ExpressionEffects[] effects, int last,
        AssignmentExpressionSyntax assignment, ref TranspileContext context, out int[] temps)
    {
        var writer = context.Emitter.Writer;
        temps = new int[call.Operands.Length];
        for (var i = 0; i < temps.Length; i++)
        {
            temps[i] = -1;
            if (call.Operands[i] is not { } operand || call.Parameters[i] is not { } parameter)
            {
                continue;
            }

            if (macro.TryGetNested(operand, out var inner) && inner.ResultType is { } innerType)
            {
                temps[i] = context.Emitter.SequenceTempCounter++;
                context.Emitter.Dialect.AppendType(ref context, innerType);
                writer.Append(' ');
                EvaluationOrder.AppendTempName(writer, temps[i]);
                writer.AppendLine("{};");
                if (!AppendExpansion(null, temps[i], inner, assignment, ref context))
                {
                    return false;
                }
            }
            else if (i <= last && !effects[i].IsEmpty)
            {
                temps[i] = EvaluationOrder.DeclareTemp(ref context, operand, IsPassedByReference(parameter));
            }
        }

        return true;
    }

    // <target>(<context>, <result>, <operands>): each operand in place, its temporary, or for the result the temporary
    // <resultTemp> of an inner operator macro
    private static void AppendCall(in MacroCall call, int[]? temps, int resultTemp,
        AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;

        // [WHY] The same resolver as the declaration, so the call names the renamed (and overload-numbered) function.
        CppNameResolver.AppendSymbolName(call.Target, ref context);
        using (writer.Parentheses())
        {
            if (call.ContextVariable != null)
            {
                AppendContextArgument(call.ContextVariable, call.Target.Parameters[0], assignment, ref context);
            }

            for (var i = 0; i < call.Operands.Length; i++)
            {
                if (call.Parameters[i] is not { } parameter)
                {
                    continue;
                }

                AppendSeparator(parameter.Ordinal, ref context);
                if (call.Operands[i] is { } operand)
                {
                    AppendOperand(operand, temps?[i] ?? -1, parameter, ref context);
                    continue;
                }

                // The temporary of an inner operator macro, passed to its ref or out result parameter
                writer.Append('&');
                EvaluationOrder.AppendTempName(writer, resultTemp);
            }
        }
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

        return true;
    }

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

    // The overload of <type>.<name> that a call with the operator's types (result, operands) selects, and whether it takes
    // a context; otherwise null, and <failure> completes "Operator macro target 'T.M' ..."
    private static (IMethodSymbol Method, bool HasContext)? ResolveOverload(INamedTypeSymbol type, string name,
        IMethodSymbol operatorMethod, Compilation compilation, out string failure)
    {
        failure = string.Empty;
        var operandTypes = new ITypeSymbol[operatorMethod.Parameters.Length + 1];
        operandTypes[0] = operatorMethod.ReturnType;
        for (var i = 0; i < operatorMethod.Parameters.Length; i++)
        {
            operandTypes[i + 1] = operatorMethod.Parameters[i].Type;
        }

        var applicable = ImmutableArray.CreateBuilder<(IMethodSymbol Method, bool HasContext)>();
        var reasons = new StringBuilder();
        foreach (var member in type.GetMembers(name))
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (TryApply(method, operandTypes, compilation, out var hasContext, out var reason))
            {
                applicable.Add((method, hasContext));
                continue;
            }

            reasons.Append(reasons.Length > 0 ? "; " : string.Empty)
                .Append(FormatSignature(method))
                .Append(": ")
                .Append(reason);
        }

        if (applicable.Count == 0)
        {
            var why = reasons.Length > 0 ? reasons.ToString() : "no method has that name";
            failure = $"has no overload that takes ({FormatOperands(operatorMethod)}): {why}";
            return null;
        }

        // The candidate that is better than every other one
        for (var i = 0; i < applicable.Count; i++)
        {
            var best = true;
            for (var j = 0; j < applicable.Count && best; j++)
            {
                best = i == j || CompareCandidates(applicable[i], applicable[j], operandTypes, compilation) == Left;
            }

            if (best)
            {
                return applicable[i];
            }
        }

        // Ambiguous: the candidates that no other one is better than, in declaration order
        var names = new StringBuilder();
        var tied = 0;
        var last = string.Empty;
        for (var i = 0; i < applicable.Count; i++)
        {
            var dominated = false;
            for (var j = 0; j < applicable.Count && !dominated; j++)
            {
                dominated = i != j && CompareCandidates(applicable[j], applicable[i], operandTypes, compilation) == Left;
            }

            if (dominated)
            {
                continue;
            }

            if (tied++ > 0)
            {
                names.Append(names.Length > 0 ? ", " : string.Empty).Append(last);
            }

            last = FormatSignature(applicable[i].Method);
        }

        failure = $"is ambiguous between {names} and {last}";
        return null;
    }

    // Whether <method> can take (result, operands), optionally after a context of a value type; otherwise <reason> says
    // why not
    private static bool TryApply(IMethodSymbol method, ITypeSymbol[] operandTypes, Compilation compilation,
        out bool hasContext, out string reason)
    {
        hasContext = false;
        if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic ||
            !method.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            reason = "not a static [GpuDeviceFunction] method";
            return false;
        }

        if (method.IsGenericMethod)
        {
            reason = "generic methods are not candidates";
            return false;
        }

        var parameters = method.Parameters;
        hasContext = parameters.Length == operandTypes.Length + 1 && parameters[0].Type.IsValueType;
        if (parameters.Length != operandTypes.Length && !hasContext)
        {
            reason = $"takes {parameters.Length} parameters, but the operator passes {operandTypes.Length} (or a context of a value type and {operandTypes.Length})";
            return false;
        }

        var offset = hasContext ? 1 : 0;
        for (var i = 0; i < operandTypes.Length; i++)
        {
            if (!CanPass(operandTypes[i], parameters[i + offset], isResult: i == 0, compilation, out reason))
            {
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    // Whether <from> passes to <parameter> (C# spec §12.6.4.2): a value parameter takes an implicit conversion; a
    // by-reference parameter takes only its own type (G# passes the address), and only the result may be ref or out
    private static bool CanPass(ITypeSymbol from, IParameterSymbol parameter, bool isResult, Compilation compilation,
        out string reason)
    {
        reason = string.Empty;
        if (parameter.RefKind != RefKind.None)
        {
            var keyword = FormatRefKind(parameter.RefKind).TrimEnd();
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && !isResult)
            {
                reason = $"'{keyword}' parameter '{parameter.Name}' is not the result";
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(from, parameter.Type))
            {
                reason = $"'{keyword}' parameter '{parameter.Name}' takes only '{FormatType(parameter.Type)}', not '{FormatType(from)}'";
                return false;
            }

            return true;
        }

        if (!compilation.ClassifyConversion(from, parameter.Type).IsImplicit)
        {
            reason = $"'{FormatType(from)}' does not convert to '{FormatType(parameter.Type)}' for '{parameter.Name}'";
            return false;
        }

        return true;
    }

    // Left when <first> is the better function member, Right when <second> is, otherwise Neither: Roslyn's
    // OverloadResolution.BetterFunctionMember, reduced to an operator macro call (one argument of a known type per
    // result and operand; no params, optional or generic candidates). The context is not an argument and is not compared
    // (decision V8). C# spec §12.6.4.3.
    private static int CompareCandidates((IMethodSymbol Method, bool HasContext) first,
        (IMethodSymbol Method, bool HasContext) second, ITypeSymbol[] operandTypes, Compilation compilation)
    {
        var result = Neither;
        var allSame = true;
        for (var i = 0; i < operandTypes.Length; i++)
        {
            var p1 = first.Method.Parameters[i + (first.HasContext ? 1 : 0)];
            var p2 = second.Method.Parameters[i + (second.HasContext ? 1 : 0)];
            allSame &= SymbolEqualityComparer.Default.Equals(p1.Type, p2.Type);
            var better = BetterConversionFromType(operandTypes[i], p1.Type, p2.Type, compilation);
            if (better == Neither)
            {
                continue;
            }

            // "for each argument, the implicit conversion from EX to QX is not better than the implicit conversion
            // from EX to PX, and for at least one argument, the conversion from EX to PX is better"
            if (result != Neither && result != better)
            {
                return Neither;
            }

            result = better;
        }

        // The tie-breakers apply only when the parameter type sequences are the same; of Roslyn's, the one an operator
        // macro can reach is preferValOverInOrRefInterpolatedHandlerParameters: a value parameter is better than an
        // 'in' or 'ref readonly' parameter for the same argument
        return result != Neither || !allSame ? result : PreferValueOverIn(first, second, operandTypes.Length);
    }

    // C# spec §12.6.4.5 (Roslyn BetterConversionFromExpression) for an argument of type <from>: a conversion to the type
    // that <from> is (exactly matches) is better; otherwise the better conversion target decides
    private static int BetterConversionFromType(ITypeSymbol from, ITypeSymbol t1, ITypeSymbol t2, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(t1, t2))
        {
            return Neither;
        }

        if (SymbolEqualityComparer.Default.Equals(from, t1))
        {
            return Left;
        }

        return SymbolEqualityComparer.Default.Equals(from, t2) ? Right : BetterConversionTarget(t1, t2, compilation);
    }

    // C# spec §12.6.4.7, Roslyn OverloadResolution.BetterConversionTargetCore:
    //   "Given two different types T1 and T2, T1 is a better conversion target than T2 if no implicit conversion from T2 to
    //   T1 exists, and at least one of the following holds: An implicit conversion from T1 to T2 exists ... T1 is a
    //   signed integral type and T2 is an unsigned integral type."
    // The Task<T>, delegate and span cases cannot occur for device function parameters.
    private static int BetterConversionTarget(ITypeSymbol t1, ITypeSymbol t2, Compilation compilation)
    {
        var oneToTwo = compilation.ClassifyConversion(t1, t2).IsImplicit;
        var twoToOne = compilation.ClassifyConversion(t2, t1).IsImplicit;
        if (oneToTwo != twoToOne)
        {
            return oneToTwo ? Left : Right;
        }

        if (oneToTwo)
        {
            // An implicit conversion both ways
            return Neither;
        }

        if (IsSignedIntegral(t1) && IsUnsignedIntegral(t2))
        {
            return Left;
        }

        return IsUnsignedIntegral(t1) && IsSignedIntegral(t2) ? Right : Neither;
    }

    private static bool IsSignedIntegral(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    private static bool IsUnsignedIntegral(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    // Left when <first> passes some argument by value where <second> takes it as 'in' or 'ref readonly' (and never the
    // other way round), Right for the reverse, otherwise Neither
    private static int PreferValueOverIn((IMethodSymbol Method, bool HasContext) first,
        (IMethodSymbol Method, bool HasContext) second, int count)
    {
        var result = Neither;
        for (var i = 0; i < count; i++)
        {
            var refKind1 = first.Method.Parameters[i + (first.HasContext ? 1 : 0)].RefKind;
            var refKind2 = second.Method.Parameters[i + (second.HasContext ? 1 : 0)].RefKind;
            var preferred = refKind1 == RefKind.None && refKind2 is RefKind.In or RefKind.RefReadOnlyParameter ? Left
                : refKind2 == RefKind.None && refKind1 is RefKind.In or RefKind.RefReadOnlyParameter ? Right
                : Neither;
            if (preferred == Neither)
            {
                continue;
            }

            if (result != Neither && result != preferred)
            {
                return Neither;
            }

            result = preferred;
        }

        return result;
    }

    // Whether the assignment target passes to the result parameter of the chosen overload. The overload is chosen by the
    // operator's return type (decision V7), so a target of another type is checked here
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

    // OverloadValue result, OverloadValue value, int shift
    private static string FormatOperands(IMethodSymbol operatorMethod)
    {
        var text = new StringBuilder();
        text.Append(FormatType(operatorMethod.ReturnType)).Append(" result");
        foreach (var parameter in operatorMethod.Parameters)
        {
            text.Append(", ").Append(FormatType(parameter.Type)).Append(' ').Append(parameter.Name);
        }

        return text.ToString();
    }

    // Shift(ref OverloadCtx, uint[], uint[], int)
    private static string FormatSignature(IMethodSymbol method) => method.FormatSignature();

    private static string FormatRefKind(RefKind refKind) => refKind.FormatRefKind();

    // uint[], OverloadCtx: the short C# spelling of a type in a message
    private static string FormatType(ITypeSymbol type) => IteratorTypeResolver.FormatType(type);

    private static (ExpressionSyntax? Left, ExpressionSyntax? Right, ExpressionSyntax? Extra) ExtractOperands(
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

    /// <summary>
    /// Finds the variable that a call of <paramref name="targetMethod"/> passes as its context: the one parameter or local
    /// of the type of the first parameter that is in scope at <paramref name="assignment"/>. Without a single one,
    /// <paramref name="failure"/> is the message argument of GSH0003.
    /// </summary>
    /// <remarks>
    /// [WHY] The operator has no place for the context, so it is found by type, as Scala resolves a <c>using</c> parameter
    /// and Kotlin a context parameter: no candidate or several candidates is an error rather than a guess. C# itself
    /// passes no implicit argument; its nearest construct, <c>[InterpolatedStringHandlerArgument]</c>, names the
    /// argument in an attribute, which would add public API.
    /// </remarks>
    private static bool TryFindContextVariable(
        AssignmentExpressionSyntax assignment,
        IMethodSymbol targetMethod,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out ISymbol? variable,
        [NotNullWhen(false)] out string? failure)
    {
        variable = null;
        failure = null;
        var contextType = targetMethod.Parameters[0].Type;
        var candidates = ImmutableArray.CreateBuilder<ISymbol>();

        // A lowered node is looked up at its place in the user's source, where the semantic model has its scope
        var location = semanticModel.GetReportLocation(assignment);
        if (location.SourceTree == semanticModel.SyntaxTree)
        {
            var position = location.SourceSpan.Start;
            semanticModel.LookupSymbols(position)
                .AsStackEnumerable()
                .Where((Type: contextType, Position: position), static (symbol, s) =>
                    IsContextCandidate(symbol, s.Type, s.Position)
                ).AddTo(candidates);
        }

        if (candidates.Count == 1)
        {
            variable = candidates[0];
            return true;
        }

        var target = $"{targetMethod.ContainingType.Name}.{targetMethod.Name}";
        var type = contextType.Name;
        failure = candidates.Count == 0
            ? $"Operator macro target '{target}' takes a context of type '{type}', but no parameter or local of that type is in scope"
            : $"Operator macro target '{target}' takes a context of type '{type}', but {candidates.Count} parameters or locals of that type are in scope ({FormatNames(candidates)})";
        return false;
    }

    // A parameter, or a local declared before the call; the scope of a local covers its whole block
    private static bool IsContextCandidate(ISymbol symbol, ITypeSymbol contextType, int position) => symbol switch
    {
        IParameterSymbol parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, contextType),
        ILocalSymbol local => SymbolEqualityComparer.Default.Equals(local.Type, contextType) &&
                              local.Locations.Length > 0 &&
                              local.Locations[0].SourceSpan.End <= position,
        _ => false
    };

    private static string FormatNames(ImmutableArray<ISymbol>.Builder candidates)
    {
        var names = new StringBuilder();
        foreach (var candidate in candidates)
        {
            names.Append(names.Length > 0 ? ", '" : "'").Append(candidate.Name).Append('\'');
        }

        return names.ToString();
    }

    // <operand> or &<operand> (or its temporary), after a comma unless it is the first argument; nothing when the
    // operator has no such operand
    private static void AppendOperand(ExpressionSyntax operand, int temp, IParameterSymbol parameter,
        ref TranspileContext context)
    {
        if (temp >= 0)
        {
            context.Emitter.Writer.AppendIf(
                IsPassedByReference(parameter) && RefSyntaxHelper.NeedsAddressOf(operand, context.Query.SemanticModel),
                "&");
            EvaluationOrder.AppendTempName(context.Emitter.Writer, temp);
            return;
        }

        if (IsPassedByReference(parameter))
        {
            RefSyntaxHelper.TranspileAsByRefArgument(ref context, operand);
            return;
        }

        context.Visitor.Visit(operand);
    }

    // The context variable as RoslynToCudaVisitor.VisitIdentifierName and RefSyntaxHelper emit a reference to it
    private static void AppendContextArgument(ISymbol variable, IParameterSymbol parameter,
        AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var writer = context.Emitter.Writer;

        // [WHY] Lowering moves a local that lives across a barrier into the state machine frame and rewrites every
        // reference to it; the call is not one of those references, so it copies one of them.
        if (FindLoweredReference(assignment, variable, model) is { } reference)
        {
            if (IsPassedByReference(parameter) && RefSyntaxHelper.NeedsAddressOf(reference, model))
            {
                writer.Append('&');
            }

            context.Visitor.Visit(reference);
            return;
        }

        if (IsPassedByReference(parameter))
        {
            writer.Append('&');
        }

        if (context.Emitter.TryGetOverride(variable.Name, out var overrideValue))
        {
            overrideValue.WriteTo(context.Visitor);
            return;
        }

        if (variable is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } or ILocalSymbol { IsRef: true } ||
            (variable is IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter, ContainingSymbol: IMethodSymbol paramMethod } &&
             !paramMethod.HasAttribute(KnownAttributes.GpuKernel)))
        {
            using (writer.Parentheses())
            {
                writer.Append('*');
                CppNameResolver.AppendSymbolName(variable, ref context);
            }

            return;
        }

        CppNameResolver.AppendSymbolName(variable, ref context);
    }

    // A reference to variable in the lowered body that holds assignment (__gsharp_sm_ctx-><name> when hoisted), or null
    // when the body is the user's own tree or does not refer to it
    private static ExpressionSyntax? FindLoweredReference(AssignmentExpressionSyntax assignment, ISymbol variable,
        SemanticModel semanticModel)
    {
        if (assignment.SyntaxTree == semanticModel.SyntaxTree)
        {
            return null;
        }

        var reference = assignment.SyntaxTree.GetRoot()
            .DescendantNodes()
            .AsStackEnumerable()
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault((Model: semanticModel, Variable: variable), static (identifier, s) =>
                SymbolEqualityComparer.Default.Equals(
                    s.Model.GetSymbol(identifier) ?? s.Model.GetDeclaredSymbol<ISymbol>(identifier),
                    s.Variable
                )
            );

        return reference?.Parent is MemberAccessExpressionSyntax access && access.Name == reference
            ? access
            : reference;
    }

    private static bool IsPassedByReference(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnlyParameter;

    private static void AppendSeparator(int parameterIndex, ref TranspileContext context)
    {
        if (parameterIndex > 0)
        {
            context.Emitter.Writer.Append(',');
        }
    }
}
