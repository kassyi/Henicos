using Kassyi.GSharp.Compiler.Analysis;

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

        if (SpanWriteClassifier.TryFailUninvoked(node, ref context))
        {
            return;
        }

        if (symbol is IPropertySymbol spanProperty && TryTranspileSpanProperty(node, spanProperty, ref context))
        {
            return;
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
        if (TryTranspileEnumAccess(memberExprSymbol, node, ref context) ||
            TryTranspileTypeQualifiedAccess(memberExprSymbol, symbol, node, ref context) ||
            TryTranspileReducedExtensionMethodGroup(node, symbol, ref context) ||
            TryTranspileStackArrayLength(node, ref context) ||
            TryTranspileSpecialArrayMembers(node, ref context))
        {
            return;
        }

        TranspileMemberAccess(node, symbol, ref context);
    }

    // Span<T>.Empty, Span<T>.IsEmpty and the Span / ReadOnlySpan properties that G# does not support.
    private static bool TryTranspileSpanProperty(
        MemberAccessExpressionSyntax node,
        IPropertySymbol property,
        ref TranspileContext context)
    {
        switch (SpanWriteClassifier.ClassifyProperty(property))
        {
            case SpanPropertyKind.Empty:
                context.Emitter.Dialect.AppendType(ref context, property.Type);
                context.Emitter.Writer.Append("{}");
                return true;
            case SpanPropertyKind.IsEmpty:
                StackArrayLocals.AppendView(node.Expression, ref context);
                context.Emitter.Writer.Append(".IsEmpty()");
                return true;
            case SpanPropertyKind.Unsupported:
                context.Reporter.Fail(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    SpanWriteClassifier.FormatUnsupportedProperty(property)
                );
                context.Visitor.AppendUnsupportedPlaceholder(node);
                return true;
            default:
                return false;
        }
    }

    // A member reached through a type name (Type.Member).
    private static bool TryTranspileTypeQualifiedAccess(
        ISymbol? memberExprSymbol,
        ISymbol? symbol,
        MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (memberExprSymbol is not ITypeSymbol)
        {
            return false;
        }

        // A device function carries its containing type in its own global name; other static members drop it.
        if (symbol is IMethodSymbol method && CppNameResolver.IsEmittedDeviceFunction(method, context.Emitter.Dialect))
        {
            DynamicSharedMemoryEmitter.FailIfReferencedWithoutCall(node, method, ref context);
            context.Emitter.Writer.AppendCppName(CppNameResolver.Resolve(method, method.Name, context.Emitter.Dialect));
            return true;
        }

        FailIfHostStaticMember(node, symbol, ref context);
        context.Emitter.Writer.Append(node.Name.Identifier.Text);
        return true;
    }

    private static void FailIfHostStaticMember(MemberAccessExpressionSyntax node, ISymbol? symbol, ref TranspileContext context)
    {
        if (symbol != null &&
            GpuCodeConstraints.IsHostStaticMemberReference(node, symbol, context.Query.SemanticModel, context.Emitter.Dialect))
        {
            context.Reporter.Fail(
                GSharpDiagnostics.StaticMemberAccessNotSupported,
                node,
                symbol.ContainingType?.Name ?? string.Empty,
                symbol.Name
            );
        }
    }

    // [WHY] A reduced extension method group (x.M) evaluates and binds its receiver when the delegate is formed, so
    // assigning a new value to x afterwards does not change the receiver that the delegate passes to M (#940).
    private static bool TryTranspileReducedExtensionMethodGroup(
        MemberAccessExpressionSyntax node,
        ISymbol? symbol,
        ref TranspileContext context)
    {
        if (!TryGetReducedExtensionMethodGroup(node, symbol, context.Query.SemanticModel, context.Emitter.Dialect,
                out var method, out var extension))
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

    // An uninvoked x.M whose M is an extension method that G# emits as a device function.
    private static bool TryGetReducedExtensionMethodGroup(
        MemberAccessExpressionSyntax node,
        ISymbol? symbol,
        SemanticModel semanticModel,
        ITranspilerDialect dialect,
        [NotNullWhen(true)] out IMethodSymbol? method,
        [NotNullWhen(true)] out IMethodSymbol? extension)
    {
        method = null;
        extension = null;
        if (node.Parent is InvocationExpressionSyntax parentInvocation && parentInvocation.Expression == node)
        {
            return false;
        }

        if ((symbol ?? semanticModel.GetSymbol(node.Name)) is not IMethodSymbol { ReducedFrom: { } reducedFrom } reduced ||
            !CppNameResolver.IsEmittedDeviceFunction(reducedFrom, dialect))
        {
            return false;
        }

        method = reduced;
        extension = reducedFrom;
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
                var parameterType = parameter.Type;
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

    // memory.Span on System.Memory<T> / System.ReadOnlyMemory<T>: the memory is already a span in C++.
    private static bool TryTranspileSpecialArrayMembers(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (!IsMemorySpanAccess(node, context.Query.SemanticModel))
        {
            return false;
        }

        context.Visitor.Visit(node.Expression);
        return true;
    }

    private static bool IsMemorySpanAccess(MemberAccessExpressionSyntax node, SemanticModel semanticModel) =>
        node.Name.Identifier.Text == "Span" &&
        semanticModel.GetTypeSymbol(node.Expression) is INamedTypeSymbol
        {
            Name: "Memory" or "ReadOnlyMemory",
            ContainingNamespace.IsGlobalNamespace: false
        } memoryType &&
        memoryType.ContainingNamespace.IsSystemNamespace();

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

    // <receiver>.<member> or <receiver>-><member>
    private static void TranspileMemberAccess(MemberAccessExpressionSyntax node, ISymbol? memberSymbol,
        ref TranspileContext context)
    {
        var member = PlanMemberName(node, memberSymbol, context.Query.SemanticModel, context.Emitter.Dialect);
        AppendMemberReceiver(node, memberSymbol, ref context);
        context.Emitter.Writer.Append(node.IsKind(SyntaxKind.PointerMemberAccessExpression) ? "->" : ".");
        WriteMemberName(node, member, ref context);
    }

    // A member of System.Array (Length, Rank, ...) throws NullReferenceException on a null array in C# (#706). A stack
    // array has no members in C++; its span members are called on a view of it (s.Slice(1))
    private static void AppendMemberReceiver(MemberAccessExpressionSyntax node, ISymbol? memberSymbol,
        ref TranspileContext context)
    {
        if (!ArrayNullCheck.TryAppendMemberReceiver(node, memberSymbol, ref context))
        {
            StackArrayLocals.AppendView(node.Expression, ref context);
        }
    }

    /// <summary>How the member name after the receiver is written.</summary>
    private enum MemberNameShape : byte
    {
        /// <summary>A name that <see cref="CppNameResolver"/> renames, followed by the getter call of a computed property.</summary>
        Resolved,

        /// <summary><c>length</c> of the array that a lowered <c>foreach</c> iterates.</summary>
        ForEachArrayLength,

        /// <summary>The member name that the dialect maps the member to.</summary>
        DialectMember,

        /// <summary>The method that the dialect maps the member to, called with no arguments.</summary>
        DialectMethodCall,

        /// <summary>The C# name, unchanged.</summary>
        Verbatim
    }

    /// <param name="Shape">The form of the output.</param>
    /// <param name="CppName">The resolved name (<see cref="MemberNameShape.Resolved"/>, <see cref="MemberNameShape.Verbatim"/>).</param>
    /// <param name="MappedName">The dialect spelling (<see cref="MemberNameShape.DialectMember"/>, <see cref="MemberNameShape.DialectMethodCall"/>).</param>
    /// <param name="Symbol">The member symbol, for the getter call of <see cref="MemberNameShape.Resolved"/>.</param>
    private readonly record struct MemberNamePlan(MemberNameShape Shape, CppName CppName, string MappedName, ISymbol? Symbol);

    private static MemberNamePlan PlanMemberName(MemberAccessExpressionSyntax node, ISymbol? memberSymbol,
        SemanticModel semanticModel, ITranspilerDialect dialect)
    {
        // [WHY] Members of structs that G# emits are resolved before the dialect mapping, which would otherwise rewrite
        // a user field named Length or Count as if it were an array length.
        // The name is resolved first because the access symbol of an invoked delegate is its Invoke method, and lowering
        // synthesizes context accesses (ctx->local) whose member carries the hoisted local's original span.
        var symbol = semanticModel.GetSymbol(node.Name) ??
                     semanticModel.GetDeclaredSymbol<ISymbol>(node.Name) ??
                     memberSymbol;
        var name = CppNameResolver.Resolve(symbol, node.Name.Identifier.ValueText, dialect);
        if (name.Kind != CppNameKind.Verbatim)
        {
            return new MemberNamePlan(MemberNameShape.Resolved, name, string.Empty, symbol);
        }

        if (name.Name == "Length" &&
            node.Expression.HasAnnotations(LoweringAnnotations.ForEachArraySourceAnnotationKind))
        {
            return new MemberNamePlan(MemberNameShape.ForEachArrayLength, name, string.Empty, symbol);
        }

        if (semanticModel.GetTypeSymbol(node.Expression) is { } typeSymbol &&
            dialect.TryMapMemberAccess(typeSymbol, name.Name, out var mappedName, out var isMethodCall) &&
            mappedName != null)
        {
            var shape = isMethodCall ? MemberNameShape.DialectMethodCall : MemberNameShape.DialectMember;
            return new MemberNamePlan(shape, name, mappedName, symbol);
        }

        return new MemberNamePlan(MemberNameShape.Verbatim, name, string.Empty, symbol);
    }

    private static void WriteMemberName(MemberAccessExpressionSyntax node, in MemberNamePlan member,
        ref TranspileContext context)
    {
        switch (member.Shape)
        {
            case MemberNameShape.Resolved:
                context.Emitter.Writer.AppendCppName(member.CppName);
                ComputedPropertyGetterCall.AppendCallIfGetter(node, member.Symbol, ref context);
                break;
            case MemberNameShape.ForEachArrayLength:
                context.Emitter.Writer.Append("length");
                break;
            case MemberNameShape.DialectMember:
                context.Emitter.Writer.Append(member.MappedName);
                break;
            case MemberNameShape.DialectMethodCall:
                context.Emitter.Writer.Append(member.MappedName).Append("()");
                break;
            case MemberNameShape.Verbatim:
                context.Emitter.Writer.AppendCppName(member.CppName);
                break;
            default:
                throw new InvalidOperationException($"Unknown member name shape {member.Shape}.");
        }
    }

    /// <param name="Count">The size when it is known at compile time.</param>
    /// <param name="Expression">The size expression of the declaration, when <paramref name="Count"/> is not known.</param>
    private readonly record struct StackArraySize(int? Count, ExpressionSyntax? Expression);

    // s.Length of a stack array local: the size its declaration gives.
    private static bool TryTranspileStackArrayLength(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (!TryGetStackArraySize(node, context.Query.SemanticModel, out var size))
        {
            return false;
        }

        WriteStackArraySize(size, ref context);
        return true;
    }

    private static bool TryGetStackArraySize(MemberAccessExpressionSyntax node, SemanticModel semanticModel,
        out StackArraySize size)
    {
        size = default;
        if (node.Name.Identifier.Text != "Length" ||
            semanticModel.GetSymbol(node.Expression) is not ILocalSymbol local ||
            !local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
            declarator.Initializer == null ||
            !InitializationSyntaxExtractor.TryGetArrayInitialization(declarator.Initializer.Value, out var count,
                out var sizeExpression, out _))
        {
            return false;
        }

        if (count.HasValue)
        {
            size = new StackArraySize(count, null);
            return true;
        }

        if (sizeExpression == null)
        {
            return false;
        }

        // A constant size is written as its value, as the declaration writes it: a const local it names is not
        // declared again when the state machine resumes
        size = semanticModel.GetSafeConstantValue(sizeExpression) is { HasValue: true, Value: int constant }
            ? new StackArraySize(constant, null)
            : new StackArraySize(null, sizeExpression);
        return true;
    }

    private static void WriteStackArraySize(in StackArraySize size, ref TranspileContext context)
    {
        if (size.Count is { } count)
        {
            context.Emitter.Writer.Append(count);
            return;
        }

        var expression = size.Expression ??
                         throw new InvalidOperationException("Stack array size without a count or an expression.");
        context.Visitor.Visit(expression);
    }
}
