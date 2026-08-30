namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# expressions (operators, invocations, member accesses, and struct creation) to CUDA C++.</summary>
/// <remarks><para>Implements: <b>SYN-02, SYN-04, SYN-05, SYN-06, SYN-07, SYN-08, SYN-09, SYN-11, SYN-12, SYN-13, SYN-14, SYN-15, SYN-16, SYN-17, SYN-19, SYN-21, SYN-22, SYN-25, SYN-26, SYN-27, SYN-28, SYN-29, SYN-31, SYN-32, SYN-34, SYN-35, SYN-36, SYN-37, SYN-38, SYN-39, SYN-40, SYN-41, SYN-42, SYN-43, SYN-44, SYN-45, SYN-46, SYN-47, SYN-48, SYN-49, SYN-50, SYN-57, SYN-58, SYN-59, SYN-60, SYN-61, SYN-62, SYN-63, SYN-64, SYN-65, SYN-66, SYN-67, SYN-68, SYN-69, SYN-73, SYN-75, SYN-76, SYN-77, SYN-78, SYN-80, SYN-81, SYN-82, SYN-83, SYN-86, SYN-87, SYN-88, SYN-90, SYN-92, SYN-93, SYN-94</b></para></remarks>
internal static class ExpressionTranspilers
{
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
                context.Writer.AppendKeyword("return");
                context.Writer.AppendSpace();
                context.Visitor.Visit(expressionBody);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }
        }
    }

    public static void TranspileAssignmentExpression(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            using (context.Writer.Scope())
            {
                for (var i = 0; i < rightTuple.Arguments.Count; i++)
                {
                    context.Writer.Append($"auto _t{i} = ");
                    context.Visitor.Visit(rightTuple.Arguments[i].Expression);
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }

                for (var i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    context.Visitor.Visit(leftTuple.Arguments[i].Expression);
                    context.Writer.Append($" = _t{i}");
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
            }

            context.Writer.AppendParentheses();
            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            context.Writer.AppendSpace();
            context.Writer.Append("-> decltype(auto) ");
            using (context.Writer.Scope())
            {
                context.Writer.AppendKeyword("auto");
                context.Writer.AppendOperator("&");
                context.Writer.AppendSpace();
                context.Writer.Append("__coalesce_target");
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("=");
                context.Writer.AppendSpace();
                context.Visitor.Visit(node.Left);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();

                context.Writer.AppendKeyword("return");
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("!");
                context.Writer.Append("__coalesce_target.HasValue");
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("?");
                context.Writer.AppendSpace();
                using (context.Writer.Parentheses())
                {
                    context.Writer.Append("__coalesce_target");
                    context.Writer.AppendSpace();
                    context.Writer.AppendOperator("=");
                    context.Writer.AppendSpace();
                    context.Visitor.Visit(node.Right);
                }

                context.Writer.AppendSpace();
                context.Writer.AppendOperator(":");
                context.Writer.AppendSpace();
                context.Writer.Append("__coalesce_target.Value");
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }

            context.Writer.AppendParentheses();

            return;
        }

        context.Visitor.Visit(node.Left);
        context.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }

    public static void TranspileBinaryExpression(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            context.Writer.AppendSpace();
            using (context.Writer.Scope())
            {
                context.Writer.AppendKeyword("auto");
                context.Writer.AppendSpace();
                context.Writer.Append("__coalesce_tmp");
                context.Writer.AppendSpace();
                context.Writer.AppendOperator("=");
                context.Writer.AppendSpace();
                context.Visitor.Visit(node.Left);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();

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

            context.Writer.AppendParentheses();

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

            // Handle 'ref' conditionals if present
            if (node.WhenTrue is RefExpressionSyntax refTrue)
            {
                context.Visitor.Visit(refTrue.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenTrue);
            }

            context.Writer.AppendSpace();
            context.Writer.AppendOperator(":");
            context.Writer.AppendSpace();

            if (node.WhenFalse is RefExpressionSyntax refFalse)
            {
                context.Visitor.Visit(refFalse.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenFalse);
            }
        }
    }

    public static void TranspileSwitchExpression(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            context.Writer.AppendSpace();
            using (context.Writer.Scope())
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
                        context.Writer.AppendKeyword("return");

                        context.Writer.AppendSpace();
                        context.Visitor.Visit(arm.Expression);
                        context.Writer.AppendSemiColon();
                        context.Writer.AppendLine();
                    }
                }
            }

            context.Writer.AppendParentheses();
        }
    }

    public static void TranspileElementAccessExpression(ElementAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        using (context.Writer.Brackets())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i].Expression);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
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
            context.Writer.AppendType(context.SemanticModel.GetTypeInfo(node.Type).Type);
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
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol;

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol;
        }

        if (symbol != null)
        {
            if (CudaIntrinsicsMapper.TryMap(symbol, out var mappedName))
            {
                context.Writer.Append(mappedName);
                return;
            }

            if ((symbol.ContainingType?.Name is TypeConstants.MathType or TypeConstants.MathFType
                    or TypeConstants.InterlockedType) &&
                (symbol.ContainingNamespace.IsSystemNamespace() || symbol.ContainingNamespace.IsSystemThreading()))
            {
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node.GetLocation(),
                    $"{symbol.ContainingType.Name}.{symbol.Name}"));
                context.Writer.Append($"__gsharp_unsupported_{symbol.Name}");
                return;
            }
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            context.Writer.AppendConstantField(field);
            return;
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
        if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
        {
            foreach (var rule in s_invocationRules)
            {
                if (rule.TryTranspile(node, ref context, symbol))
                {
                    return;
                }
            }
        }

        context.Visitor.Visit(node.Expression);
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }


    public static void TranspileArgument(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            context.Writer.Append("&");
        }

        context.Visitor.Visit(node.Expression);
    }

    public static void TranspileObjectCreationExpression(ObjectCreationExpressionSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        TryTranspileObjectCreationHelper(typeSymbol, node, node.Initializer, node.ArgumentList, ref context);
    }

    public static void TranspileImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
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

        if (typeSymbol is { TypeKind: TypeKind.Struct } ||
            (typeSymbol != null && typeSymbol.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>"))
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
        using (context.Writer.Parentheses())
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            context.Writer.AppendSpace();
            using (context.Writer.Scope())
            {
                if (typeSymbol.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
                {
                    var capacity = "64";
                    if (argumentList != null && argumentList.Arguments.Count == 1)
                    {
                        var arg = argumentList.Arguments[0].Expression;
                        if (arg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression))
                        {
                            capacity = literal.Token.ValueText;
                        }
                    }
                    var namedType = (INamedTypeSymbol)typeSymbol;
                    context.Writer.Append("System_Collections_Generic_List<");
                    context.Writer.AppendType(namedType.TypeArguments[0]);
                    context.Writer.Append($", {capacity}> _tmp;");
                }
                else
                {
                    context.Writer.AppendType(typeSymbol);
                    context.Writer.AppendSpace();
                    context.Writer.Append("_tmp");

                    if (argumentList is { Arguments.Count: > 0 })
                    {
                        using (context.Writer.Braces())
                        {
                            var first = true;
                            foreach (var arg in argumentList.Arguments)
                            {
                                if (!first)
                                {
                                    context.Writer.AppendComma();
                                    context.Writer.AppendSpace();
                                }

                                context.Visitor.Visit(arg);
                                first = false;
                            }
                        }
                    }
                    else
                    {
                        context.Writer.AppendEmptyScope();
                    }

                    context.Writer.AppendSemiColon();
                }
                context.Writer.AppendSpace();

                foreach (var expr in initializer.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax assignment)
                    {
                        continue;
                    }

                    context.Writer.Append("_tmp.");
                    context.Visitor.Visit(assignment.Left);
                    context.Writer.AppendSpace();
                    context.Writer.AppendOperator("=");
                    context.Writer.AppendSpace();
                    context.Visitor.Visit(assignment.Right);
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendSpace();
                }

                context.Writer.AppendKeyword("return");
                context.Writer.AppendSpace();
                context.Writer.Append("_tmp");
                context.Writer.AppendSemiColon();
                context.Writer.AppendSpace();
            }

            context.Writer.AppendParentheses();
        }
    }

    private static void TranspileStructCreationWithoutInitializer(ITypeSymbol typeSymbol,
        ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        if (typeSymbol.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            var capacity = "64";
            if (argumentList != null && argumentList.Arguments.Count == 1)
            {
                var arg = argumentList.Arguments[0].Expression;
                if (arg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression))
                {
                    capacity = literal.Token.ValueText;
                }
            }
            
            var namedType = (INamedTypeSymbol)typeSymbol;
            context.Writer.Append("System_Collections_Generic_List<");
            context.Writer.AppendType(namedType.TypeArguments[0]);
            context.Writer.Append($", {capacity}>");
            context.Writer.AppendEmptyScope();
            return;
        }

        context.Writer.AppendType(typeSymbol);

        if (argumentList is { Arguments.Count: > 0 })
        {
            using (context.Writer.Scope())
            {
                var first = true;
                foreach (var arg in argumentList.Arguments)
                {
                    if (!first)
                    {
                        context.Writer.AppendComma();
                        context.Writer.AppendSpace();
                    }

                    context.Visitor.Visit(arg);
                    first = false;
                }
            }
        }
        else
        {
            context.Writer.AppendEmptyScope();
        }
    }
}






