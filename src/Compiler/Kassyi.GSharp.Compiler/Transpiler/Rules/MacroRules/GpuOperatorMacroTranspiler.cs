using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Intercepts and transpiles expressions decorated with <c>[GpuOperatorMacro]</c> into specialized device function calls.
/// </summary>
internal static class GpuOperatorMacroTranspiler
{
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
            if (macro.IsTargetMissing)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, macro.FormatMissingTarget());
            }

            return false;
        }

        ISymbol? contextVariable = null;
        if (targetMethod.Parameters.Length == macro.MaxParameterCount &&
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
    /// The <c>[GpuOperatorMacro]</c> operator that the right side of <paramref name="assignment"/> calls, and the method it
    /// maps to. The analyzer reports a missing or incompatible target (GSH0003) from the same answer.
    /// </summary>
    public static bool TryResolve(AssignmentExpressionSyntax assignment, SemanticModel semanticModel, out OperatorMacro macro)
    {
        macro = default;
        var rightNode = UnwrapExpression(assignment.Right, semanticModel);

        if (semanticModel.GetSymbol(rightNode) is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var macroAttr = methodSymbol.GetAttribute(KnownAttributes.GpuOperatorMacro);
        if (macroAttr?.AttributeClass?.ContainingNamespace.IsCoreNamespace() != true ||
            macroAttr.ConstructorArguments.Length < 2)
        {
            return false;
        }

        var targetMethod = ResolveTargetMethod(
            macroAttr,
            rightNode,
            out var targetType,
            out var targetMethodName,
            out var expectedMethodParamsMax);
        macro = new OperatorMacro(rightNode, targetType, targetMethodName, targetMethod, expectedMethodParamsMax);
        return true;
    }

    /// <summary>A call of a <c>[GpuOperatorMacro]</c> operator and the method it maps to.</summary>
    internal readonly struct OperatorMacro(
        ExpressionSyntax operatorCall,
        INamedTypeSymbol? targetType,
        string? targetMethodName,
        IMethodSymbol? targetMethod,
        int maxParameterCount)
    {
        /// <summary>The operator expression, without the conversion around it.</summary>
        public ExpressionSyntax OperatorCall { get; } = operatorCall;

        public INamedTypeSymbol? TargetType { get; } = targetType;

        public string? TargetMethodName { get; } = targetMethodName;

        /// <summary>The method the operator maps to, or <see langword="null"/> when it is missing or incompatible.</summary>
        public IMethodSymbol? TargetMethod { get; } = targetMethod;

        /// <summary>The parameter count of a target that takes a context first.</summary>
        public int MaxParameterCount { get; } = maxParameterCount;

        /// <summary>The attribute names a target, but no method of that type fits the operator.</summary>
        public bool IsTargetMissing => TargetMethod == null && TargetType != null && TargetMethodName != null;

        public string FormatMissingTarget() =>
            $"Macro target method '{TargetType?.Name}.{TargetMethodName}' is missing or incompatible.";
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is CastExpressionSyntax castExpr)
        {
            return castExpr.Expression;
        }

        var op = semanticModel.GetSafeOperation(expression);
        if (op is IConversionOperation conv)
        {
            return conv.Operand.Syntax as ExpressionSyntax ?? expression;
        }

        return expression;
    }

    private static IMethodSymbol? ResolveTargetMethod(
        AttributeData macroAttr,
        ExpressionSyntax rightNode,
        out INamedTypeSymbol? targetType,
        out string? targetMethodName,
        out int expectedMethodParamsMax)
    {
        targetType = macroAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
        targetMethodName = macroAttr.ConstructorArguments[1].Value as string;

        if (targetType == null || targetMethodName == null)
        {
            expectedMethodParamsMax = 0;
            return null;
        }

        var expectedMethodParamsMin = rightNode is BinaryExpressionSyntax ? 3 : 2; // result + left + (right/extraArg)
        expectedMethodParamsMax = expectedMethodParamsMin + 1; // + context

        return targetType.GetMembers(targetMethodName)
            .AsStackEnumerable()
            .Select(static member => member as IMethodSymbol)
            .FirstOrDefault(
                (Min: expectedMethodParamsMin, Max: expectedMethodParamsMax),
                static (m, expected) => m is not null &&
                    (m.Parameters.Length == expected.Min ||
                     (m.Parameters.Length == expected.Max && m.Parameters[0].Type.IsValueType))
            );
    }

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
