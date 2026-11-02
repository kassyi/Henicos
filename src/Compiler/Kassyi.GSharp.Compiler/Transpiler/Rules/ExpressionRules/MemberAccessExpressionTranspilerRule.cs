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

        if ((node.Parent is not InvocationExpressionSyntax parentInvocation || parentInvocation.Expression != node) &&
            SpanWriteClassifier.TryGetMethodSymbol(node, context.Query.SemanticModel) is { } uninvokedMethod &&
            SpanWriteClassifier.ClassifyMethod(uninvokedMethod) != SpanMemberKind.None)
        {
            context.Reporter.Fail(
                GSharpDiagnostics.UnsupportedSyntax,
                node,
                SpanWriteClassifier.FormatUnsupportedMethod(uninvokedMethod));
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        if (symbol is IPropertySymbol spanProperty)
        {
            switch (SpanWriteClassifier.ClassifyProperty(spanProperty))
            {
                case SpanPropertyKind.Empty:
                    context.Emitter.Dialect.AppendType(ref context, spanProperty.Type);
                    context.Emitter.Writer.Append("{}");
                    return;
                case SpanPropertyKind.IsEmpty:
                    StackArrayLocals.AppendView(node.Expression, ref context);
                    context.Emitter.Writer.Append(".IsEmpty()");
                    return;
                case SpanPropertyKind.Unsupported:
                    context.Reporter.Fail(
                        GSharpDiagnostics.UnsupportedSyntax,
                        node,
                        SpanWriteClassifier.FormatUnsupportedProperty(spanProperty));
                    context.Visitor.AppendUnsupportedPlaceholder(node);
                    return;
            }
        }

        if (symbol != null && TryTranspileIntrinsic(symbol, ref context))
        {
            return;
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            ConstantAtUse.Append(node, field.Type, field.ConstantValue, ref context);
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
                DynamicSharedMemoryEmitter.FailIfReferencedWithoutCall(node, method, ref context);
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

        if (TryTranspileReducedExtensionMethodGroup(node, symbol, ref context))
        {
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

    // [WHY] A reduced extension method group (x.M) evaluates and binds its receiver when the delegate is formed, so
    // assigning a new value to x afterwards does not change the receiver that the delegate passes to M (#940).
    private static bool TryTranspileReducedExtensionMethodGroup(
        MemberAccessExpressionSyntax node,
        ISymbol? symbol,
        ref TranspileContext context)
    {
        if (node.Parent is InvocationExpressionSyntax parentInvocation && parentInvocation.Expression == node)
        {
            return false;
        }

        if ((symbol ?? context.Query.SemanticModel.GetSymbol(node.Name)) is not IMethodSymbol { ReducedFrom: { } extension } method ||
            !CppNameResolver.IsEmittedDeviceFunction(extension, context.Emitter.Dialect))
        {
            return false;
        }

        DynamicSharedMemoryEmitter.FailIfReferencedWithoutCall(node, extension, ref context);
        if (!InvocationExpressionTranspilerRule.FailIfSuspendingExtension(node, extension, ref context))
        {
            WriteBoundExtensionLambda(node.Expression, method, extension, ref context);
        }

        return true;
    }

    // [<receiver param> = (<receiver>)](<parameters>) { return <extension>(<receiver param>, <parameters>); }
    private static void WriteBoundExtensionLambda(
        ExpressionSyntax receiver,
        IMethodSymbol method,
        IMethodSymbol extension,
        ref TranspileContext context)
    {
        StructTranspiler.RegisterUsedStructIfApplicable(method.ReturnType, ref context);
        using (context.Emitter.Writer.Brackets())
        {
            CppNameResolver.AppendSymbolName(extension.Parameters[0], ref context);
            context.Emitter.Writer.Append(" = ");
            using (context.Emitter.Writer.Parentheses())
            {
                StackArrayLocals.AppendView(receiver, ref context);
            }
        }

        using (context.Emitter.Writer.Parentheses())
        {
            for (var i = 0; i < method.Parameters.Length; i++)
            {
                context.Emitter.Writer.AppendIf(i > 0, ", ");
                var parameter = method.Parameters[i];
                var parameterType = parameter.Type.UnwrapNullableArrayLike();
                StructTranspiler.RegisterUsedStructIfApplicable(parameterType, ref context);
                context.Emitter.Dialect.AppendType(ref context, parameterType);
                context.Emitter.Writer.AppendIf(parameter.RefKind != RefKind.None, "*");
                context.Emitter.Writer.Append(' ');
                CppNameResolver.AppendSymbolName(parameter, ref context);
            }
        }

        context.Emitter.Writer.Append(' ');
        using (context.Emitter.Writer.InlineScope("{ ", " }"))
        {
            context.Emitter.Writer.AppendIf(!extension.ReturnsVoid, "return ");
            CppNameResolver.AppendSymbolName(extension, ref context);
            using (context.Emitter.Writer.Parentheses())
            {
                CppNameResolver.AppendSymbolName(extension.Parameters[0], ref context);
                for (var i = 0; i < method.Parameters.Length; i++)
                {
                    context.Emitter.Writer.Append(", ");
                    CppNameResolver.AppendSymbolName(method.Parameters[i], ref context);
                }
            }

            context.Emitter.Writer.Append(';');
        }
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

    /// <summary>
    /// Writes the native name that the dialect maps <paramref name="symbol"/> to (<c>[CudaIntrinsic]</c>, an intrinsic
    /// pattern or the dialect YAML), with the template arguments of a generic intrinsic.
    /// </summary>
    /// <returns><see langword="false"/>, writing nothing, when the dialect does not map the symbol.</returns>
    internal static bool TryTranspileIntrinsic(ISymbol symbol, ref TranspileContext context)
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
        // A member of System.Array (Length, Rank, ...) throws NullReferenceException on a null array in C# (#706). A
        // stack array has no members in C++; its span members are called on a view of it (s.Slice(1))
        if (!ArrayNullCheck.TryAppendMemberReceiver(node, memberSymbol, ref context))
        {
            StackArrayLocals.AppendView(node.Expression, ref context);
        }

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

        if (memberName.Name == "Length" &&
            node.Expression.HasAnnotations(LoweringAnnotations.ForEachArraySourceAnnotationKind))
        {
            context.Emitter.Writer.Append("length");
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

            // A constant size is written as its value, as the declaration writes it: a const local it names is not
            // declared again when the state machine resumes
            if (sizeExpr != null &&
                context.Query.SemanticModel.GetSafeConstantValue(sizeExpr) is { HasValue: true, Value: int size })
            {
                context.Emitter.Writer.Append(size);
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
