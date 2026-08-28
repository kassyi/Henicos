namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# expressions (operators, invocations, member accesses, and struct creation) to CUDA C++.</summary>
internal static class ExpressionTranspilers
{
    public static void TranspileAssignmentExpression(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
            context.Writer.Append("[&]()");
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

        context.Visitor.Visit(node.Left);
        context.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }

    public static void TranspileBinaryExpression(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Left);
            context.Writer.AppendOperator(node.OperatorToken.Text);
            context.Visitor.Visit(node.Right);
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
            context.Writer.Append("*");
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
            context.Writer.Append(CudaTypeMapper.MapType(context.SemanticModel.GetTypeInfo(node.Type).Type));
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

        var exprSymbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
        if (node.Expression is IdentifierNameSyntax idName &&
            context.LocalVariableOverrides.TryGetValue(idName.Identifier.Text, out var overrideValue) &&
            overrideValue.NodeValue != null)
        {
            exprSymbol = context.SemanticModel.GetSymbolInfo(overrideValue.NodeValue).Symbol;
        }

        if (symbol != null)
        {
            if (CudaIntrinsicsMapper.TryMap(symbol, out var mappedName))
            {
                context.Writer.Append(mappedName);
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
        context.Writer.Append(node.Name.Identifier.Text);
    }

    private static readonly ImmutableArray<Rules.IInvocationTranspilerRule> s_invocationRules =
        ImmutableArray.Create<Rules.IInvocationTranspilerRule>(
            Rules.BclIntrinsicTranspilerRule.Instance,
            Rules.GpuMacroTranspilerRule.Instance
        );


    public static void TranspileInvocationExpression(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;

        if (symbol != null)
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
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace)
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
        var cppType = CudaTypeMapper.MapType(typeSymbol);

        if (initializer != null)
        {
            TranspileStructCreationWithInitializer(cppType, initializer, argumentList, ref context);
        }
        else
        {
            TranspileStructCreationWithoutInitializer(cppType, argumentList, ref context);
        }
    }

    private static void TranspileStructCreationWithInitializer(string cppType, InitializerExpressionSyntax initializer,
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
                context.Writer.Append(cppType);
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

    private static void TranspileStructCreationWithoutInitializer(string cppType, ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        context.Writer.Append(cppType);

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






