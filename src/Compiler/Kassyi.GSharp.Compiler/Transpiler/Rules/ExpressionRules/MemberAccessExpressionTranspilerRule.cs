namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class MemberAccessExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<MemberAccessExpressionSyntax>
{
    public override void Transpile(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = context.Query.SemanticModel.GetSymbol(node);

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.Query.SemanticModel.GetSymbol(invocation);
        }

        if (symbol != null && TryTranspileIntrinsic(symbol, ref context))
        {
            return;
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            context.Emitter.Writer.AppendConstantField(field);
            return;
        }

        var memberExprSymbol = context.Query.SemanticModel.GetSymbol(node.Expression);
        if (TryTranspileEnumAccess(memberExprSymbol, node, ref context))
        {
            return;
        }

        if (memberExprSymbol is ITypeSymbol)
        {
            // A device function carries its containing type in its own global name; other static members drop it.
            if (symbol is IMethodSymbol method && CppNameResolver.IsEmittedDeviceFunction(method, context.Emitter.Dialect))
            {
                context.Emitter.Writer.AppendCppName(CppNameResolver.Resolve(method, method.Name, context.Emitter.Dialect));
                return;
            }

            if (symbol != null &&
                GpuCodeConstraints.IsHostStaticMemberReference(node, symbol, context.Query.SemanticModel, context.Emitter.Dialect))
            {
                context.Reporter.Fail(
                    GSharpDiagnostics.StaticMemberAccessNotSupported,
                    node,
                    symbol.ContainingType?.Name ?? string.Empty,
                    symbol.Name);
            }

            context.Emitter.Writer.Append(node.Name.Identifier.Text);
            return;
        }

        if (TryTranspileStackArrayLength(node, ref context))
        {
            return;
        }

        if (TryTranspileSpecialArrayMembers(node, ref context))
        {
            return;
        }

        TranspileMemberAccess(node, symbol, ref context);
    }

    private static bool TryTranspileSpecialArrayMembers(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        var memberName = node.Name.Identifier.Text;
        var operandType = context.Query.SemanticModel.GetTypeSymbol(node.Expression);

        if (operandType == null)
        {
            return false;
        }

        if (memberName == "Span" &&
            operandType is INamedTypeSymbol { Name: "Memory" or "ReadOnlyMemory", ContainingNamespace.IsGlobalNamespace: false } nsSymbol &&
            nsSymbol.ContainingNamespace.IsSystemNamespace())
        {
            context.Visitor.Visit(node.Expression);
            return true;
        }

        if (operandType.IsNullableArrayLike())
        {
            if (memberName == "Value")
            {
                context.Visitor.Visit(node.Expression);
                return true;
            }

            if (memberName == "HasValue")
            {
                NullRepresentation.AppendArrayNullTest(node.Expression, isNull: false, ref context);
                return true;
            }
        }

        return false;
    }

    private static bool TryTranspileIntrinsic(ISymbol symbol, ref TranspileContext context)
    {
        if (context.Emitter.Dialect.TryMapIntrinsic(symbol, out var mappedName))
        {
            context.Emitter.Writer.Append(mappedName);
            AppendIntrinsicTemplateArguments(symbol, ref context);
            return true;
        }

        return false;
    }

    // <native name><<type argument>, <type argument>>
    // [WHY] A generic [CudaIntrinsic] method maps to a C++ function template. It is called with the type arguments that
    // C# bound (written or inferred), so that C++ deduction cannot pick other types.
    private static void AppendIntrinsicTemplateArguments(ISymbol symbol, ref TranspileContext context)
    {
        if (symbol is not IMethodSymbol { IsGenericMethod: true } method ||
            !method.OriginalDefinition.HasAttribute(KnownAttributes.CudaIntrinsic))
        {
            return;
        }

        using (context.Emitter.Writer.InlineScope("<", ">"))
        {
            for (var i = 0; i < method.TypeArguments.Length; i++)
            {
                context.Emitter.Writer.AppendIf(i > 0, ", ");
                context.Emitter.Dialect.AppendType(ref context, method.TypeArguments[i]);
            }
        }
    }

    private static bool TryTranspileEnumAccess(ISymbol? memberExprSymbol, MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (memberExprSymbol is ITypeSymbol { TypeKind: TypeKind.Enum })
        {
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.Append($"::{node.Name.Identifier.Text}");
            return true;
        }

        return false;
    }

    private static void TranspileMemberAccess(MemberAccessExpressionSyntax node, ISymbol? memberSymbol,
        ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        context.Emitter.Writer.Append(node.IsKind(SyntaxKind.PointerMemberAccessExpression) ? "->" : ".");

        // [WHY] Members of structs that G# emits are resolved before the dialect mapping, which would otherwise rewrite
        // a user field named Length or Count as if it were an array length.
        // The name is resolved first because the access symbol of an invoked delegate is its Invoke method, and lowering
        // synthesizes context accesses (ctx->local) whose member carries the hoisted local's original span.
        memberSymbol = context.Query.SemanticModel.GetSymbol(node.Name) ??
                       context.Query.SemanticModel.GetDeclaredSymbol<ISymbol>(node.Name) ??
                       memberSymbol;
        var memberName = CppNameResolver.Resolve(memberSymbol, node.Name.Identifier.ValueText, context.Emitter.Dialect);
        if (memberName.Kind != CppNameKind.Verbatim)
        {
            context.Emitter.Writer.AppendCppName(memberName);
            ComputedPropertyGetterCall.AppendCallIfGetter(node, memberSymbol, ref context);
            return;
        }

        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Expression);
        if (typeSymbol != null && context.Emitter.Dialect.TryMapMemberAccess(typeSymbol, memberName.Name, out var mappedName, out var isMethodCall) && mappedName != null)
        {
            if (isMethodCall)
            {
                context.Emitter.Writer.Append(mappedName).Append("()");
            }
            else
            {
                context.Emitter.Writer.Append(mappedName);
            }
        }
        else
        {
            context.Emitter.Writer.AppendCppName(memberName);
        }
    }

    private static bool TryTranspileStackArrayLength(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (node.Name.Identifier.Text != "Length")
        {
            return false;
        }

        var symbol = context.Query.SemanticModel.GetSymbol(node.Expression);
        if (symbol is ILocalSymbol localSym)
        {
            if (!localSym.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var decl))
            {
                return false;
            }

            if (decl.Initializer == null)
            {
                return false;
            }

            if (!InitializationSyntaxExtractor.TryGetArrayInitialization(decl.Initializer.Value, out var count, out var sizeExpr,
                    out _))
            {
                return false;
            }

            if (count.HasValue)
            {
                context.Emitter.Writer.Append(count.Value);
                return true;
            }

            if (sizeExpr != null)
            {
                context.Visitor.Visit(sizeExpr);
                return true;
            }
        }

        return false;
    }
}
