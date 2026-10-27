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

        if (macro.TargetMethod is not { } targetMethod)
        {
            if (macro.Failure is { } targetFailure)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, targetFailure);
            }

            return false;
        }

        ISymbol? contextVariable = null;
        if (macro.HasContext &&
            !TryFindContextVariable(assignment, targetMethod, context.Query.SemanticModel, out contextVariable,
                out var failure))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, failure);
            return false;
        }

        var (leftOp, rightOp, extraArg) = ExtractOperands(macro.OperatorCall);
        var firstOperandParameter = contextVariable != null ? 1 : 0;
        ExpressionSyntax?[] operands = [assignment.Left, leftOp, rightOp, extraArg];
        var temps = PlanOperandTemps(operands, targetMethod, firstOperandParameter, context.Query.SemanticModel);

        // C# evaluates the target and then the operands from left to right; when they interfere, they are evaluated
        // into temporaries in that order first (EvaluationOrder)
        using (var sequence = temps != null ? EvaluationOrder.BeginSequence(context.Emitter.Writer) : default)
        {
            if (temps != null)
            {
                DeclareOperandTemps(operands, temps, targetMethod, firstOperandParameter, ref context);
                sequence.Return();
            }

            // [WHY] The same resolver as the declaration, so the call names the renamed (and overload-numbered) function.
            CppNameResolver.AppendSymbolName(targetMethod, ref context);
            using (context.Emitter.Writer.Parentheses())
            {
                var parameterIndex = 0;
                if (contextVariable != null)
                {
                    AppendContextArgument(contextVariable, targetMethod.Parameters[parameterIndex++], assignment,
                        ref context);
                }

                for (var i = 0; i < operands.Length; i++)
                {
                    AppendOperand(operands[i], temps?[i] ?? -1, targetMethod, ref parameterIndex, ref context);
                }
            }
        }

        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // The temporary of each operand (-1: emitted in place), or null when no two operands interfere
    private static int[]? PlanOperandTemps(ExpressionSyntax?[] operands, IMethodSymbol targetMethod,
        int firstOperandParameter, SemanticModel model)
    {
        var effects = new ExpressionEffects[operands.Length];
        var parameterIndex = firstOperandParameter;
        for (var i = 0; i < operands.Length; i++)
        {
            if (operands[i] is not { } operand)
            {
                continue;
            }

            effects[i] = IsPassedByReference(targetMethod.Parameters[parameterIndex++])
                ? ExpressionEffects.OfLocation(operand, model)
                : ExpressionEffects.Of(operand, model);
        }

        var last = EvaluationOrder.LastSpilledOperand(effects);
        if (last < 0)
        {
            return null;
        }

        var temps = new int[operands.Length];
        for (var i = 0; i < temps.Length; i++)
        {
            temps[i] = i <= last && operands[i] != null && !effects[i].IsEmpty ? 0 : -1;
        }

        return temps;
    }

    // auto&& __gsharp_seq_<n> = <operand passed by reference>; | auto __gsharp_seq_<n> = <operand>;
    private static void DeclareOperandTemps(ExpressionSyntax?[] operands, int[] temps, IMethodSymbol targetMethod,
        int firstOperandParameter, ref TranspileContext context)
    {
        var parameterIndex = firstOperandParameter;
        for (var i = 0; i < operands.Length; i++)
        {
            if (operands[i] is not { } operand)
            {
                continue;
            }

            var byReference = IsPassedByReference(targetMethod.Parameters[parameterIndex++]);
            if (temps[i] >= 0)
            {
                temps[i] = EvaluationOrder.DeclareTemp(ref context, operand, byReference);
            }
        }
    }

    /// <summary>
    /// The <c>[GpuOperatorMacro]</c> operator that the right side of <paramref name="assignment"/> calls, and the overload
    /// of its target that the call maps to. The analyzer reports a target that cannot be resolved (GSH0003) from the same
    /// answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overload is chosen as C# overload resolution would choose it for a call with the operator's types (C# spec
    /// §12.6.4, Roslyn <c>OverloadResolution</c>), not by declaration order (#478): the result is the operator's return
    /// type and the operands are its parameter types. A candidate is a static <c>[GpuDeviceFunction]</c>
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
        var operatorCall = UnwrapExpression(assignment.Right);
        if (GetOperatorMethod(operatorCall, semanticModel) is not { } operatorMethod)
        {
            return false;
        }

        var macroAttr = operatorMethod.GetAttribute(KnownAttributes.GpuOperatorMacro);
        if (macroAttr?.AttributeClass?.ContainingNamespace.IsCoreNamespace() != true ||
            macroAttr.ConstructorArguments.Length < 2)
        {
            return false;
        }

        if (macroAttr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType ||
            macroAttr.ConstructorArguments[1].Value is not string targetMethodName)
        {
            macro = new OperatorMacro(operatorCall, null, false, null);
            return true;
        }

        var target = $"{targetType.Name}.{targetMethodName}";
        if (ResolveOverload(targetType, targetMethodName, operatorMethod, semanticModel.Compilation, out var failure) is not
            var (method, hasContext))
        {
            macro = new OperatorMacro(operatorCall, null, false, $"Operator macro target '{target}' {failure}");
            return true;
        }

        var resultParameter = method.Parameters[hasContext ? 1 : 0];
        if (!AcceptsTarget(assignment.Left, resultParameter, semanticModel))
        {
            var name = semanticModel.GetSymbol(assignment.Left) is { } symbol ? $"'{symbol.Name}' " : string.Empty;
            macro = new OperatorMacro(operatorCall, null, false,
                $"The target {name}of operator macro '{target}' does not convert to '{FormatType(resultParameter.Type)}' of parameter '{resultParameter.Name}'");
            return true;
        }

        macro = new OperatorMacro(operatorCall, method, hasContext, null);
        return true;
    }

    /// <summary>A call of a <c>[GpuOperatorMacro]</c> operator and the method it maps to.</summary>
    internal readonly struct OperatorMacro(
        ExpressionSyntax operatorCall,
        IMethodSymbol? targetMethod,
        bool hasContext,
        string? failure)
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
    }

    // The operator call inside the parentheses and casts written around it: c = (T)(a + b) calls + (#641)
    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    break;
                default:
                    return expression;
            }
        }
    }

    // The user-defined operator that a binary or prefix operator expression calls
    private static IMethodSymbol? GetOperatorMethod(ExpressionSyntax operatorCall, SemanticModel semanticModel)
    {
        if (operatorCall is not (BinaryExpressionSyntax or PrefixUnaryExpressionSyntax))
        {
            return null;
        }

        var operation = semanticModel.GetSafeOperation(operatorCall);
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            _ => null
        };
    }

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
    private static string FormatSignature(IMethodSymbol method)
    {
        var text = new StringBuilder();
        text.Append(method.Name).Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            text.Append(i > 0 ? ", " : string.Empty)
                .Append(FormatRefKind(parameter.RefKind))
                .Append(FormatType(parameter.Type));
        }

        return text.Append(')').ToString();
    }

    private static string FormatRefKind(RefKind refKind) => refKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => string.Empty
    };

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
    private static void AppendOperand(ExpressionSyntax? operand, int temp, IMethodSymbol targetMethod,
        ref int parameterIndex, ref TranspileContext context)
    {
        if (operand == null)
        {
            return;
        }

        AppendSeparator(parameterIndex, ref context);
        var parameter = targetMethod.Parameters[parameterIndex++];
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
