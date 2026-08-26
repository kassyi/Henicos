namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class ExpressionTranspilers
{
    public static void TranspileAssignmentExpression(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
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

            context.Writer.Append("()");
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

    public static void TranspileElementAccessExpression(ElementAccessExpressionSyntax node, ref TranspileContext context)
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

    public static void TranspileParenthesizedExpression(ParenthesizedExpressionSyntax node, ref TranspileContext context)
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
            context.Writer.Append(field.ConstantValue!.ToString());
            switch (field.Type.SpecialType)
            {
                case SpecialType.System_UInt64:

                    context.Writer.Append("ULL");
                    break;
                case SpecialType.System_UInt32:

                    context.Writer.Append("U");
                    break;
                case SpecialType.System_Int64:

                    context.Writer.Append("LL");
                    break;
            }

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

        var isPointer = exprSymbol is IParameterSymbol pSym &&
                        (pSym.RefKind == RefKind.Ref || pSym.RefKind == RefKind.Out);

        context.Writer.Append(isPointer ? "->" : ".");
        context.Writer.Append(node.Name.Identifier.Text);
    }


    public static void TranspileInvocationExpression(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;

        if (symbol is { ContainingType.Name: "Gpu", Name: "InlinePtx" })
        {
            TranspileInlinePtx(node, ref context);
            return;
        }

        if (symbol is { ContainingType.Name: "BitOperations" } &&
            symbol.ContainingType.ToString() == "System.Numerics.BitOperations")
        {
            TranspileBitOperationsInvocation(node, ref context, symbol);
            return;
        }

        if (symbol is { ContainingType.Name: "Gpu", Name: "JitConstant" })
        {
            if (node.ArgumentList.Arguments.Count == 1)
            {
                var expr = node.ArgumentList.Arguments[0].Expression;
                if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    context.Writer.Append(literal.Token.ValueText);
                }
                else if (expr is IdentifierNameSyntax identifier)
                {
                    context.Writer.Append(identifier.Identifier.ValueText);
                }
            }

            return;
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

    private static void TranspileBitOperationsInvocation(InvocationExpressionSyntax node, ref TranspileContext context,
        IMethodSymbol symbol)
    {
        var is64Bit = false;
        if (symbol.Parameters.Length > 0)
        {
            var paramType = symbol.Parameters[0].Type;
            if (paramType.TypeKind == TypeKind.Enum)
            {
                paramType = ((INamedTypeSymbol)paramType).EnumUnderlyingType ?? paramType;
            }

            var platform = context.SemanticModel.Compilation.Options.Platform;
            var is64BitPointer = platform == Platform.X64 || platform == Platform.Arm64 ||
                                 platform == Platform.Itanium || platform == Platform.AnyCpu;

            is64Bit = paramType.SpecialType == SpecialType.System_UInt64 ||
                      paramType.SpecialType == SpecialType.System_Int64 ||
                      (is64BitPointer &&
                       (paramType.SpecialType == SpecialType.System_UIntPtr ||
                        paramType.SpecialType == SpecialType.System_IntPtr ||
                        paramType.Name == "nuint" ||
                        paramType.Name == "nint"));
        }

        var functionName = symbol.Name switch
        {
            "LeadingZeroCount" => is64Bit ? "__gsharp_lzc_64" : "__gsharp_lzc_32",
            "PopCount" => is64Bit ? "__popcll" : "__popc",
            "TrailingZeroCount" => is64Bit ? "__gsharp_tzc_64" : "__gsharp_tzc_32",
            "Log2" => is64Bit ? "__gsharp_log2_64" : "__gsharp_log2_32",
            "RotateLeft" => is64Bit ? "__gsharp_rotl64" : "__gsharp_rotl32",
            "RotateRight" => is64Bit ? "__gsharp_rotr64" : "__gsharp_rotr32",
            _ => throw new InvalidOperationException($"Unsupported BitOperations method: {symbol.Name}")
        };

        context.Writer.Append(functionName);
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                if (symbol.Name == "PopCount" && i == 0)
                {
                    using (context.Writer.Parentheses())
                    {
                        context.Writer.AppendKeyword("unsigned");
                        context.Writer.AppendSpace();
                        if (is64Bit)
                        {
                            context.Writer.AppendKeyword("long");
                            context.Writer.AppendSpace();
                            context.Writer.AppendKeyword("long");
                        }
                        else
                        {
                            context.Writer.AppendKeyword("int");
                        }
                    }
                    using (context.Writer.Parentheses())
                    {
                        context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                    }
                }
                else
                {
                    context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                }

                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }

    private static void TranspileInlinePtx(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments.Count != 1)
        {
            context.ReportDiagnostic?.Invoke(
                Diagnostic.Create(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, node.GetLocation()));
            return;
        }

        var argExpr = node.ArgumentList.Arguments[0].Expression;

        var constantValue = context.SemanticModel.GetConstantValue(argExpr);
        if (constantValue is { HasValue: true, Value: string ptxString })
        {
            context.Writer.Append(ptxString);
            context.Writer.AppendLine();
            return;
        }

        if (argExpr is InterpolatedStringExpressionSyntax interpolated)
        {
            TranspileInterpolatedInlinePtx(interpolated, ref context);
            return;
        }

        context.ReportDiagnostic?.Invoke(
            Diagnostic.Create(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, node.GetLocation()));
    }


    private static void TranspileInterpolatedInlinePtx(InterpolatedStringExpressionSyntax interpolated,
        ref TranspileContext context)
    {
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    context.Writer.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    context.Visitor.Visit(interpolation.Expression);
                    break;
            }
        }

        context.Writer.AppendLine();
    }

    public static void TranspileArgument(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            var symbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
            var isAlreadyPointer = symbol is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } ||
                                   symbol is ILocalSymbol { IsRef: true };

            if (!isAlreadyPointer)
            {
                context.Writer.Append("&");
            }
        }

        context.Visitor.Visit(node.Expression);
    }

    public static void TranspileObjectCreationExpression(ObjectCreationExpressionSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        if (typeSymbol is { Name: TypeConstants.SharedMemoryType } &&
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace)
        {
            context.Visitor.DefaultVisit(node);
            return;
        }

        if (typeSymbol is { TypeKind: TypeKind.Struct })
        {
            TranspileStructCreationCore(typeSymbol, node.Initializer, node.ArgumentList, ref context);
            return;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported,
            node.GetLocation()));
        context.Visitor.DefaultVisit(node);
    }

    public static void TranspileImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        switch (typeSymbol)
        {
            case { Name: TypeConstants.SharedMemoryType } when
                typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace:
                context.Visitor.DefaultVisit(node);

                return;
            case { TypeKind: TypeKind.Struct }:

                TranspileStructCreationCore(typeSymbol, node.Initializer, node.ArgumentList, ref context);
                return;
            default:
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported,
                    node.GetLocation()));
                context.Visitor.DefaultVisit(node);
                break;
        }
    }

    private static void TranspileStructCreationCore(ITypeSymbol typeSymbol, InitializerExpressionSyntax? initializer,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        var cppType = CudaTypeMapper.MapType(typeSymbol);
        if (initializer != null)
        {
            using (context.Writer.Parentheses())
            {
                using (context.Writer.Brackets())
                {
                    context.Writer.Append("&");
                }

                using (context.Writer.Parentheses())
                {
                }

                context.Writer.AppendSpace();
                using (context.Writer.Scope())
                {
                    context.Writer.Append($"{cppType} _tmp; ");
                    foreach (var expr in initializer.Expressions)
                    {
                        if (expr is AssignmentExpressionSyntax assignment)
                        {
                            context.Writer.Append("_tmp.");
                            context.Visitor.Visit(assignment.Left);
                            context.Writer.Append(" = ");
                            context.Visitor.Visit(assignment.Right);
                            context.Writer.Append("; ");
                        }
                    }

                    context.Writer.Append("return _tmp; ");
                }

                using (context.Writer.Parentheses())
                {
                }
            }

            return;
        }

        if (argumentList is { Arguments.Count: > 0 })
        {
            context.Writer.Append(cppType);
            using (context.Writer.Scope())
            {
                var first = true;
                foreach (var arg in argumentList.Arguments)
                {
                    if (!first)
                    {
                        context.Writer.Append(", ");
                    }

                    context.Visitor.Visit(arg.Expression);
                    first = false;
                }
            }

            return;
        }

        context.Writer.Append(cppType);
    }
}






