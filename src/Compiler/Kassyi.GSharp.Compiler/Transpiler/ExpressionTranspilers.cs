namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# expressions (operators, invocations, member accesses, and struct creation) to CUDA C++.</summary>
/// <remarks><para>Implements: <b>SYN-02, SYN-04, SYN-05, SYN-06, SYN-07, SYN-08, SYN-09, SYN-11, SYN-12, SYN-13, SYN-14, SYN-15, SYN-16, SYN-17, SYN-19, SYN-21, SYN-22, SYN-25, SYN-26, SYN-27, SYN-28, SYN-29, SYN-31, SYN-32, SYN-34, SYN-35, SYN-36, SYN-37, SYN-38, SYN-39, SYN-40, SYN-41, SYN-42, SYN-43, SYN-44, SYN-45, SYN-46, SYN-47, SYN-48, SYN-49, SYN-50, SYN-57, SYN-58, SYN-59, SYN-60, SYN-61, SYN-62, SYN-63, SYN-64, SYN-65, SYN-66, SYN-67, SYN-68, SYN-69, SYN-73, SYN-75, SYN-76, SYN-77, SYN-78, SYN-80, SYN-81, SYN-82, SYN-83, SYN-86, SYN-87, SYN-88, SYN-90, SYN-92, SYN-93, SYN-94</b></para></remarks>
internal static class ExpressionTranspilers
{
    public static void TranspileThisExpression(ThisExpressionSyntax node, ref TranspileContext context) =>
        context.Writer.Append("(*this)");

    public static void
        TranspileSimpleLambdaExpression(SimpleLambdaExpressionSyntax node, ref TranspileContext context) =>
        TranspileLambda(node.Parameter, node.Block, node.ExpressionBody, ref context);

    public static void TranspileParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node,
        ref TranspileContext context) =>
        TranspileLambda(node.ParameterList, node.Block, node.ExpressionBody, ref context);

    public static void TranspileAnonymousMethodExpression(AnonymousMethodExpressionSyntax node,
        ref TranspileContext context) =>
        TranspileLambda(node.ParameterList, node.Block, node.ExpressionBody, ref context);

    private static void TranspileLambda(
        SyntaxNode? parameterOrList,
        BlockSyntax? block,
        ExpressionSyntax? expressionBody,
        ref TranspileContext context)
    {
        context.Writer.Append("[&]");

        if (parameterOrList != null)
        {
            if (parameterOrList is ParameterSyntax parameter)
            {
                // Simple lambda has no parentheses around its single parameter
                using (context.Writer.Parentheses())
                {
                    context.Visitor.Visit(parameter);
                }
            }
            else
            {
                context.Visitor.Visit(parameterOrList);
            }
        }
        else
        {
            context.Writer.AppendParentheses();
        }

        context.Writer.AppendSpace();

        if (block != null)
        {
            using (context.Writer.Scope())
            {
                foreach (var stmt in block.Statements)
                {
                    context.Visitor.Visit(stmt);
                    context.Writer.AppendLine();
                }
            }
        }
        else if (expressionBody != null)
        {
            using (context.Writer.Scope())
            {
                context.EmitReturn(expressionBody);
            }
        }
    }

    public static void TranspileAssignmentExpression(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            TranspileTupleToTupleAssignment(leftTuple, rightTuple, node.SpanStart, ref context);
            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            TranspileCoalesceAssignment(node, ref context);
            return;
        }

        if (IsDiscardAssignment(node, ref context))
        {
            context.Visitor.Visit(node.Right);
            return;
        }

        if (node.OperatorToken.IsKind(SyntaxKind.EqualsToken))
        {
            if (node.Left is TupleExpressionSyntax tupleLeft)
            {
                var isStatement = node.Parent is ExpressionStatementSyntax;
                TranspileTupleDeconstructionAssignment(tupleLeft.Arguments.Select(a => (SyntaxNode)a.Expression),
                    node.Right, isDeclaration: isStatement, ref context);
                return;
            }

            if (node.Left is DeclarationExpressionSyntax
                {
                    Designation: ParenthesizedVariableDesignationSyntax parenthesizedDesignation
                })
            {
                TranspileTupleDeconstructionAssignment(parenthesizedDesignation.Variables, node.Right,
                    isDeclaration: true, ref context);
                return;
            }
        }

        context.Visitor.Visit(node.Left);
        context.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }

    private static bool IsDiscardAssignment(AssignmentExpressionSyntax node, ref TranspileContext context) =>
        node.Left.IsDiscard(context.SemanticModel);

    private static void TranspileTupleToTupleAssignment(
        TupleExpressionSyntax leftTuple,
        TupleExpressionSyntax rightTuple,
        int spanStart,
        ref TranspileContext context)
    {
        // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
        using (context.Writer.ImmediatelyInvokedLambda())
        {
            for (var i = 0; i < rightTuple.Arguments.Count; i++)
            {
                context.EmitAutoAssignment($"_t_{spanStart}_{i}", rightTuple.Arguments[i].Expression);
            }

            for (var i = 0; i < leftTuple.Arguments.Count; i++)
            {
                var leftExpr = leftTuple.Arguments[i].Expression;
                if (leftExpr.IsDiscard(context.SemanticModel))
                {
                    continue;
                }

                context.Visitor.Visit(leftExpr);
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("=");
                context.Writer.AppendSpace();
                context.Writer.Append($"_t_{spanStart}_{i}");
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }
        }
    }

    private static void TranspileCoalesceAssignment(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        var targetVar = $"__coalesce_target_{node.SpanStart}";

        using (context.Writer.ImmediatelyInvokedLambda("decltype(auto)"))
        {
            context.EmitAutoAssignment(targetVar, node.Left, isRef: true);

            context.Writer.AppendKeyword("return");
            context.Writer.AppendSpace();
            context.Writer.AppendOperator("!");
            context.Writer.Append($"{targetVar}.HasValue");
            context.Writer.AppendSpace();
            context.Writer.AppendOperator("?");
            context.Writer.AppendSpace();
            using (context.Writer.Parentheses())
            {
                context.Writer.Append(targetVar);
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("=");
                context.Writer.AppendSpace();
                context.Visitor.Visit(node.Right);
            }

            context.Writer.AppendSpace();
            context.Writer.AppendOperator(":");
            context.Writer.AppendSpace();
            context.Writer.Append($"{targetVar}.Value");
            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();
        }
    }

    private static void TranspileTupleDeconstructionAssignment(
        IEnumerable<SyntaxNode> leftVariables,
        ExpressionSyntax rightNode,
        bool isDeclaration,
        ref TranspileContext context)
    {
        if (!isDeclaration)
        {
            using (context.Writer.ImmediatelyInvokedLambda())
            {
                EmitTupleDeconstructionBody(leftVariables, rightNode, isInsideLambda: true, ref context);
            }
        }
        else
        {
            EmitTupleDeconstructionBody(leftVariables, rightNode, isInsideLambda: false, ref context);
        }
    }

    private static void EmitTupleDeconstructionBody(
        IEnumerable<SyntaxNode> leftVariables,
        ExpressionSyntax rightNode,
        bool isInsideLambda,
        ref TranspileContext context)
    {
        var tmpName = $"__tuple_tmp_{rightNode.SpanStart}";
        context.EmitAutoAssignment(tmpName, rightNode);

        var index = 1;
        foreach (var varNode in leftVariables)
        {
            EmitDeconstructionTarget(varNode, $"{tmpName}.Item{index}", ref context);
            index++;
        }

        if (isInsideLambda)
        {
            context.EmitReturn(tmpName);
        }
    }

    private static void EmitDeconstructionTarget(SyntaxNode varNode, string sourceExpression,
        ref TranspileContext context)
    {
        var unwrapped = varNode.UnwrapDesignation();
        var isHoisted = varNode is DeclarationExpressionSyntax && unwrapped is SingleVariableDesignationSyntax;
        varNode = unwrapped;

        if (varNode.IsDiscard(context.SemanticModel))
        {
            return;
        }

        switch (varNode)
        {
            case SingleVariableDesignationSyntax singleDesignation:
            {
                if (!isHoisted)
                {
                    context.Writer.AppendKeyword("auto");
                    context.Writer.AppendSpace();
                }

                context.Writer.Append(singleDesignation.Identifier.Text);
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("=");
                context.Writer.AppendSpace();
                context.Writer.Append(sourceExpression);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
                break;
            }
            case ParenthesizedVariableDesignationSyntax nestedDesignation:
            {
                // Extract the nested tuple into a local tmp
                var nestedTmp = $"__nested_tmp_{nestedDesignation.SpanStart}";
                context.EmitAutoAssignment(nestedTmp, sourceExpression);

                var index = 1;
                foreach (var nestedVar in nestedDesignation.Variables)
                {
                    EmitDeconstructionTarget(nestedVar, $"{nestedTmp}.Item{index}", ref context);
                    index++;
                }

                break;
            }
            case TupleExpressionSyntax nestedTupleExpr:
            {
                // Extract the nested tuple into a local tmp
                var nestedTmp = $"__nested_tmp_{nestedTupleExpr.SpanStart}";
                context.EmitAutoAssignment(nestedTmp, sourceExpression);

                var index = 1;
                foreach (var arg in nestedTupleExpr.Arguments)
                {
                    EmitDeconstructionTarget(arg.Expression, $"{nestedTmp}.Item{index}", ref context);
                    index++;
                }

                break;
            }
            default:
                context.Visitor.Visit(varNode);
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("=");
                context.Writer.AppendSpace();
                context.Writer.Append(sourceExpression);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
                break;
        }
    }

    public static void TranspileBinaryExpression(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            using (context.Writer.ImmediatelyInvokedLambda())
            {
                context.EmitAutoAssignment("__coalesce_tmp", node.Left);

                context.Writer.AppendKeyword("return");
                context.Writer.AppendSpace();
                context.Writer.Append("__coalesce_tmp.HasValue");
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("?");
                context.Writer.AppendSpace();
                context.Writer.Append("__coalesce_tmp.Value");
                context.Writer.AppendSpace();
                context.Writer.AppendOperator(":");
                context.Writer.AppendSpace();
                context.Visitor.Visit(node.Right);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }

            return;
        }

        context.Visitor.Visit(node.Left);

        var opText = node.OperatorToken.Text;
        if (opText.Length > 0 && char.IsLetter(opText[0]))
        {
            context.Writer.AppendSpace();
            context.Writer.AppendOperator(opText);
            context.Writer.AppendSpace();
        }
        else
        {
            context.Writer.AppendOperator(opText);
        }

        context.Visitor.Visit(node.Right);
    }

    public static void TranspileConditionalExpression(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
            context.Writer.AppendSpace();
            context.Writer.AppendOperator("?");
            context.Writer.AppendSpace();
            context.Visitor.Visit(node.WhenTrue.UnwrapRef());
            context.Writer.AppendSpace();
            context.Writer.AppendOperator(":");
            context.Writer.AppendSpace();
            context.Visitor.Visit(node.WhenFalse.UnwrapRef());
        }
    }

    public static void TranspileSwitchExpression(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.ImmediatelyInvokedLambda())
        {
            context.Writer.AppendKeyword("switch");
            using (context.Writer.Parentheses())
            {
                context.Visitor.Visit(node.GoverningExpression);
            }

            context.Writer.AppendLine();
            using (context.Writer.Scope())
            {
                foreach (var arm in node.Arms)
                {
                    if (arm.WhenClause != null)
                    {
                        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax,
                            arm.WhenClause.GetLocation(),
                            "Switch expression arms with 'when' clauses are not supported"));
                        return;
                    }

                    switch (arm.Pattern)
                    {
                        case ConstantPatternSyntax constantPattern:
                            context.Writer.AppendKeyword("case");
                            context.Writer.AppendSpace();
                            context.Visitor.Visit(constantPattern.Expression);
                            context.Writer.Append(":");
                            break;
                        case DiscardPatternSyntax:
                            context.Writer.Append("default:");
                            break;
                        default:
                            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax,
                                arm.Pattern.GetLocation(),
                                "Switch expression patterns other than constant and discard are not supported"));
                            context.Writer.Append("default:");
                            break;
                    }

                    context.Writer.AppendSpace();
                    context.EmitReturn(arm.Expression);
                }
            }
        }
    }

    public static void TranspileElementAccessExpression(ElementAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        using (context.Writer.Brackets())
        {
            context.TranspileSeparatedList(node.ArgumentList.Arguments);
        }
    }

    public static void TranspilePrefixUnaryExpression(PrefixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            context.Writer.AppendOperator("*");
            context.Visitor.Visit(node.Operand);
            return;
        }

        var op = node.OperatorToken.Text;
        context.Writer.Append(op);
        context.Visitor.Visit(node.Operand);
    }

    public static void TranspileCastExpression(CastExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Writer.AppendType(node.Type.GetTypeSymbol(context.SemanticModel));
        }

        context.Visitor.Visit(node.Expression);
    }

    public static void TranspileParenthesizedExpression(ParenthesizedExpressionSyntax node,
        ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Expression);
        }
    }

    public static void TranspilePostfixUnaryExpression(PostfixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        context.Visitor.Visit(node.Operand);
        context.Writer.Append(node.OperatorToken.Text);
    }

    public static void TranspileMemberAccessExpression(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = node.ResolveMemberSymbol(context.SemanticModel);
        if (symbol != null)
        {
            if (CudaIntrinsicsMapper.TryMap(symbol, out var mappedName))
            {
                context.Writer.Append(mappedName);
                return;
            }

            if (IsUnsupportedBclMethod(symbol))
            {
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node.GetLocation(),
                    $"{symbol.ContainingType!.Name}.{symbol.Name}"));
                context.Writer.Append($"__gsharp_unsupported_{symbol.Name}");
                return;
            }

            if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
            {
                context.Writer.AppendConstantField(field);
                return;
            }
        }

        var memberExprSymbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
        if (memberExprSymbol is ITypeSymbol typeSym)
        {
            if (typeSym.TypeKind == TypeKind.Enum)
            {
                context.Visitor.Visit(node.Expression);
                context.Writer.Append("::");
                context.Writer.Append(node.Name.Identifier.Text);
                return;
            }

            // Skip class name for global static methods
            context.Writer.Append(node.Name.Identifier.Text);
            return;
        }

        context.Visitor.Visit(node.Expression);
        context.Writer.Append(".");

        var memberName = node.Name.Identifier.Text;
        var typeInfo = context.SemanticModel.GetTypeInfo(node.Expression);
        if (typeInfo.Type != null && typeInfo.Type.IsSystemNumericsVector())
        {
            memberName = memberName.ToLowerInvariant();
        }

        context.Writer.Append(memberName);
    }

    private static readonly ImmutableArray<Rules.IInvocationTranspilerRule> s_invocationRules =
        ImmutableArray.Create<Rules.IInvocationTranspilerRule>(
            Rules.BclIntrinsicTranspilerRule.Instance,
            Rules.GpuMacroTranspilerRule.Instance
        );


    public static void TranspileInvocationExpression(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = node.GetSymbol<IMethodSymbol>(context.SemanticModel);
        if (symbol != null)
        {
            if (TryTranspileWarpShuffle(node, symbol, ref context))
            {
                return;
            }

            if (TryTranspileReducedExtension(node, symbol, ref context))
            {
                return;
            }

            foreach (var rule in s_invocationRules)
            {
                if (rule.TryTranspile(node, ref context, symbol))
                {
                    return;
                }
            }
        }

        var semanticModel = context.SemanticModel;
        if (node.ArgumentList.Arguments.Any(a => a.IsOutDiscard(semanticModel)))
        {
            TranspileInvocationWithOutDiscard(node, symbol, ref context);
            return;
        }

        context.Visitor.Visit(node.Expression);
        using (context.Writer.Parentheses())
        {
            context.TranspileSeparatedList(node.ArgumentList.Arguments);
        }
    }

    private static bool TryTranspileWarpShuffle(InvocationExpressionSyntax node, IMethodSymbol symbol,
        ref TranspileContext context)
    {
        if (symbol is not { Name: "WarpShuffle", ContainingType.Name: "Gpu" } ||
            !symbol.ContainingNamespace.IsCoreNamespace())
        {
            return false;
        }

        context.Writer.Append("__shfl_sync");
        using (context.Writer.Parentheses())
        {
            var args = node.ArgumentList.Arguments;
            switch (args.Count)
            {
                case 2:
                    context.Writer.Append("0xFFFFFFFF");
                    context.Writer.AppendComma();
                    context.TranspileSeparatedList(args);
                    break;
                case 3 when symbol.Parameters.Length > 0 && symbol.Parameters[0].Name == "mask":
                    context.TranspileSeparatedList(args);
                    break;
                case 3:
                    context.Visitor.Visit(args[2]);
                    context.Writer.AppendComma();
                    context.Visitor.Visit(args[0]);
                    context.Writer.AppendComma();
                    context.Visitor.Visit(args[1]);
                    break;
                default:
                    context.TranspileSeparatedList(args);
                    break;
            }
        }

        return true;
    }

    private static bool TryTranspileReducedExtension(InvocationExpressionSyntax node, IMethodSymbol symbol,
        ref TranspileContext context)
    {
        if (symbol.MethodKind != MethodKind.ReducedExtension)
        {
            return false;
        }

        var receiverType = symbol.ReceiverType;
        if (receiverType is { TypeKind: TypeKind.Class })
        {
            context.ReportDiagnostic?.Invoke(receiverType.SpecialType == SpecialType.System_String
                ? Diagnostic.Create(GSharpDiagnostics.StringNotSupported, node.GetLocation())
                : Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, node.GetLocation(),
                    receiverType.ToDisplayString()));
        }

        context.Writer.Append(symbol.Name);
        using (context.Writer.Parentheses())
        {
            if (node.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (symbol.ReducedFrom?.Parameters.Length > 0 &&
                    symbol.ReducedFrom.Parameters[0].IsByRef())
                {
                    context.Writer.Append("&");
                }

                context.Visitor.Visit(memberAccess.Expression);
            }

            if (node.ArgumentList.Arguments.Count <= 0)
            {
                return true;
            }

            context.Writer.AppendComma();
            context.TranspileSeparatedList(node.ArgumentList.Arguments);
        }

        return true;
    }

    private static bool IsUnsupportedBclMethod(ISymbol symbol) =>
        (symbol.ContainingType?.Name is TypeConstants.MathType or TypeConstants.MathFType
            or TypeConstants.InterlockedType) &&
        (symbol.ContainingNamespace.IsSystemNamespace() || symbol.ContainingNamespace.IsSystemThreading());

    private static void TranspileInvocationWithOutDiscard(InvocationExpressionSyntax node, IMethodSymbol? symbol,
        ref TranspileContext context)
    {
        using (context.Writer.ImmediatelyInvokedLambda())
        {
            if (symbol != null)
            {
                // We must use semantic model to map arguments to parameters (handles named/optional)
                for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    var arg = node.ArgumentList.Arguments[i];
                    if (!arg.IsOutDiscard(context.SemanticModel))
                    {
                        continue;
                    }

                    // Look up corresponding parameter
                    var param = arg.DetermineParameter(symbol);
                    var type = param?.Type;

                    if (type != null)
                    {
                        context.Writer.AppendType(type);
                    }
                    else
                    {
                        context.Writer.Append("int"); // Fallback
                    }

                    context.Writer.Append($" __discard_{node.SpanStart}_{i}");
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
            }
            else
            {
                // Fallback for unresolved methods (e.g., dynamic or unbindable)
                for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    var arg = node.ArgumentList.Arguments[i];
                    if (!arg.IsOutDiscard(context.SemanticModel))
                    {
                        continue;
                    }

                    context.Writer.Append($"int __discard_{node.SpanStart}_{i}");
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
            }

            var isVoid = symbol?.ReturnsVoid ?? false;
            if (!isVoid)
            {
                context.Writer.AppendKeyword("return");
                context.Writer.AppendSpace();
            }

            context.Visitor.Visit(node.Expression);
            using (context.Writer.Parentheses())
            {
                for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    var arg = node.ArgumentList.Arguments[i];
                    if (arg.IsOutDiscard(context.SemanticModel))
                    {
                        if (arg.NameColon != null)
                        {
                            context.Visitor.Visit(arg.NameColon);
                        }

                        context.Writer.Append($"&__discard_{node.SpanStart}_{i}");
                    }
                    else
                    {
                        context.Visitor.Visit(arg);
                    }

                    if (i >= node.ArgumentList.Arguments.Count - 1)
                    {
                        continue;
                    }

                    context.Writer.AppendComma();
                    context.Writer.AppendSpace();
                }
            }

            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();
        }
    }


    public static void TranspileArgument(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.IsByRefOrOut())
        {
            context.Writer.Append("&");
        }

        context.Visitor.Visit(node.Expression);
    }

    public static void TranspileObjectCreationExpression(ObjectCreationExpressionSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = node.GetTypeSymbol(context.SemanticModel);
        TryTranspileObjectCreationHelper(typeSymbol, node, node.Initializer, node.ArgumentList, ref context);
    }

    public static void TranspileImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = node.GetTypeSymbol(context.SemanticModel);
        TryTranspileObjectCreationHelper(typeSymbol, node, node.Initializer, node.ArgumentList, ref context);
    }

    private static void TryTranspileObjectCreationHelper(
        ITypeSymbol? typeSymbol,
        ExpressionSyntax node,
        InitializerExpressionSyntax? initializer,
        ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        if (typeSymbol is { Name: TypeConstants.SharedMemoryType } &&
            typeSymbol.ContainingNamespace.IsCoreNamespace())
        {
            context.Visitor.DefaultVisit(node);
            return;
        }

        if (typeSymbol is { TypeKind: TypeKind.Struct })
        {
            TranspileStructCreationCore(typeSymbol, initializer, argumentList, ref context);
            return;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported,
            node.GetLocation()));
        context.Visitor.DefaultVisit(node);
    }

    private static void TranspileStructCreationCore(ITypeSymbol typeSymbol, InitializerExpressionSyntax? initializer,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

        if (initializer != null)
        {
            TranspileStructCreationWithInitializer(typeSymbol, initializer, argumentList, ref context);
        }
        else
        {
            TranspileStructCreationWithoutInitializer(typeSymbol, argumentList, ref context);
        }
    }

    private static void TranspileStructCreationWithInitializer(ITypeSymbol typeSymbol,
        InitializerExpressionSyntax initializer,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        var tmpVar = $"_tmp_{initializer.SpanStart}";

        using (context.Writer.ImmediatelyInvokedLambda())
        {
            context.Writer.AppendType(typeSymbol);
            context.Writer.AppendSpace();
            context.Writer.Append(tmpVar);

            if (argumentList is { Arguments.Count: > 0 })
            {
                using (context.Writer.Braces())
                {
                    context.TranspileSeparatedList(argumentList.Arguments, withSpace: true);
                }
            }
            else
            {
                context.Writer.AppendEmptyScope();
            }

            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();

            context.TranspileMemberAssignments(tmpVar, initializer);

            context.EmitReturn(tmpVar);
        }
    }

    private static void TranspileStructCreationWithoutInitializer(ITypeSymbol typeSymbol,
        ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        context.Writer.AppendType(typeSymbol);

        if (argumentList is { Arguments.Count: > 0 })
        {
            using (context.Writer.Scope())
            {
                context.TranspileSeparatedList(argumentList.Arguments, withSpace: true);
            }
        }
        else
        {
            context.Writer.AppendEmptyScope();
        }
    }

    public static void TranspileWithExpression(WithExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.ImmediatelyInvokedLambda())
        {
            var tmpName = $"__gsharp_with_tmp_{node.SpanStart}";
            context.EmitAutoAssignment(tmpName, node.Expression);

            context.TranspileMemberAssignments(tmpName, node.Initializer);

            context.EmitReturn(tmpName);
        }
    }
}
