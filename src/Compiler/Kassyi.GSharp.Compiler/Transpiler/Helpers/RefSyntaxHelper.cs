namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

internal enum CppLocation
{
    None,
    Storage,
    AutoPropertyStorage
}

/// <summary>
/// Provides shared logic for handling 'ref', 'out', and pointer semantics in the AST.
/// </summary>
internal static class RefSyntaxHelper
{
    /// <summary>
    /// Determines whether the given expression requires an address-of operator (&amp;) when passed as a pointer or reference argument.
    /// Returns false if the expression is already a pointer.
    /// </summary>
    /// <remarks>
    /// By-reference parameters (<c>ref</c>, <c>out</c>, <c>in</c>, <c>ref readonly</c>) and ref locals are emitted
    /// dereferenced as <c>(*x)</c> by <see cref="RoslynToCudaVisitor.VisitIdentifierName"/>, so they still require
    /// <c>&amp;</c> to recover the pointer (<c>&amp;(*x)</c>).
    /// </remarks>
    public static bool NeedsAddressOf(ExpressionSyntax expression, SemanticModel semanticModel) =>
        semanticModel.GetTypeSymbol(expression) is not IPointerTypeSymbol;

    /// <summary>
    /// Transpiles the given expression as a reference/pointer argument, automatically appending the address-of operator if needed,
    /// or wrapping a temporary rvalue in <c>__gsharp_temp_address</c>.
    /// </summary>
    public static void TranspileAsByRefArgument(ref TranspileContext context, ExpressionSyntax expression)
    {
        var semanticModel = context.Query.SemanticModel;
        if (!NeedsAddressOf(expression, semanticModel))
        {
            context.Visitor.Visit(expression);
            return;
        }

        if (HasExplicitRefSyntax(expression) || IsCppLValue(expression, semanticModel))
        {
            context.Emitter.Writer.Append("&");
            context.Visitor.Visit(expression);
            return;
        }

        // An auto-property is emitted as its backing field (a C++ lvalue of the target type), so binding const T&
        // directly in __gsharp_temp_address<T> would alias the field instead of copying the getter's return value (#999).
        var forceValueCopy = IsAutoPropertyOrMemberOfAutoProperty(expression, semanticModel);
        context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TempAddress);
        AppendTempAddressTypeArgument(ref context, expression, semanticModel);
        using (context.Emitter.Writer.Parentheses())
        {
            if (forceValueCopy)
            {
                context.Emitter.Writer.Append("static_cast");
                AppendTempAddressTypeArgument(ref context, expression, semanticModel);
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(expression);
                }
            }
            else
            {
                context.Visitor.Visit(expression);
            }
        }
    }

    /// <summary>
    /// Transpiles an operand of an operator macro passed by reference.
    /// Uses the backing field storage address for auto-properties (<see cref="CppLocation.AutoPropertyStorage"/>)
    /// to preserve aliasing without defensive copies.
    /// </summary>
    public static void TranspileAsOperatorOperand(ref TranspileContext context, ExpressionSyntax expression)
    {
        var semanticModel = context.Query.SemanticModel;
        if (!NeedsAddressOf(expression, semanticModel))
        {
            context.Visitor.Visit(expression);
            return;
        }

        if (HasExplicitRefSyntax(expression) || ClassifyLocation(expression, semanticModel) is not CppLocation.None)
        {
            context.Emitter.Writer.Append("&");
            context.Visitor.Visit(expression);
            return;
        }

        context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TempAddress);
        AppendTempAddressTypeArgument(ref context, expression, semanticModel);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(expression);
        }
    }

    private static bool HasExplicitRefSyntax(ExpressionSyntax expression) =>
        expression.Parent is ArgumentSyntax
        {
            RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword
        } or RefExpressionSyntax;

    private static TypeInfo GetSafeTypeInfo(ExpressionSyntax expression, SemanticModel semanticModel) =>
        semanticModel.GetOriginalNode(expression) is { } original
            ? semanticModel.GetTypeInfo(original)
            : default;

    private static void AppendTempAddressTypeArgument(ref TranspileContext context, ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var typeInfo = GetSafeTypeInfo(expression, semanticModel);
        var targetType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (targetType is null or IErrorTypeSymbol { TypeKind: TypeKind.Error })
        {
            return;
        }

        using (context.Emitter.Writer.InlineScope("<", ">"))
        {
            if (targetType is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } methodTypeParam)
            {
                CppNameResolver.AppendSymbolName(methodTypeParam, ref context);
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, targetType);
            }
        }
    }

    /// <summary>
    /// Classifies whether <paramref name="expression"/> is an actual storage location in C++ (<see cref="CppLocation.Storage"/>),
    /// an auto-property backing field (<see cref="CppLocation.AutoPropertyStorage"/>), or an rvalue (<see cref="CppLocation.None"/>).
    /// </summary>
    public static CppLocation ClassifyLocation(ExpressionSyntax expression, SemanticModel model)
    {
        if (IsConverted(expression, model))
        {
            return CppLocation.None;
        }

        return Unparenthesize(expression) switch
        {
            DeclarationExpressionSyntax => CppLocation.Storage,
            IdentifierNameSyntax identifier => ClassifySymbol(model.GetSymbol(identifier) ?? model.GetDeclaredSymbol<ISymbol>(identifier)),
            MemberAccessExpressionSyntax access => ClassifyMemberAccess(access, model),
            ElementAccessExpressionSyntax element => ClassifyElementAccess(element, model),
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } => CppLocation.Storage,
            ConditionalExpressionSyntax { WhenTrue: RefExpressionSyntax } or ConditionalExpressionSyntax { WhenFalse: RefExpressionSyntax } => CppLocation.Storage,
            InvocationExpressionSyntax invocation => ClassifyInvocation(invocation, model),
            _ => CppLocation.None
        };
    }

    private static bool IsConverted(ExpressionSyntax expression, SemanticModel model)
    {
        var typeInfo = GetSafeTypeInfo(expression, model);
        return typeInfo.Type != null &&
               typeInfo.ConvertedType != null &&
               !typeInfo.Type.IsEqualTo(typeInfo.ConvertedType);
    }

    private static ExpressionSyntax Unparenthesize(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static CppLocation ClassifyMemberAccess(MemberAccessExpressionSyntax access, SemanticModel model)
    {
        if (access.IsKind(SyntaxKind.PointerMemberAccessExpression))
        {
            return CppLocation.Storage;
        }

        var symbol = model.GetSymbol(access) ??
                     model.GetSymbol(access.Name) ??
                     model.GetDeclaredSymbol<ISymbol>(access.Name);
        var memberLoc = ClassifySymbol(symbol);
        if (memberLoc == CppLocation.None)
        {
            return CppLocation.None;
        }

        if (access.Expression is ThisExpressionSyntax ||
            model.GetTypeSymbol(access.Expression) is not { IsValueType: true })
        {
            return memberLoc;
        }

        var receiverLoc = ClassifyLocation(access.Expression, model);
        return WeakerOf(memberLoc, receiverLoc);
    }

    private static CppLocation WeakerOf(CppLocation a, CppLocation b)
    {
        if (a == CppLocation.None || b == CppLocation.None)
        {
            return CppLocation.None;
        }

        if (a == CppLocation.AutoPropertyStorage || b == CppLocation.AutoPropertyStorage)
        {
            return CppLocation.AutoPropertyStorage;
        }

        return CppLocation.Storage;
    }

    private static CppLocation ClassifySymbol(ISymbol? symbol) =>
        symbol switch
        {
            ILocalSymbol or IParameterSymbol => CppLocation.Storage,
            IFieldSymbol { IsConst: false, IsStatic: false } => CppLocation.Storage,
            IPropertySymbol { RefKind: RefKind.Ref or RefKind.RefReadOnly } => CppLocation.Storage,
            IPropertySymbol { ContainingType: INamedTypeSymbol containingType } property
                when GlobalCppNames.IsEmittedUserType(containingType) && !StructAbiMembers.IsComputedProperty(property) =>
                CppLocation.AutoPropertyStorage,
            _ => CppLocation.None
        };

    private static CppLocation ClassifyElementAccess(ElementAccessExpressionSyntax element, SemanticModel model)
    {
        if (model.GetSymbol(element) is IPropertySymbol indexer)
        {
            return indexer.RefKind is RefKind.Ref or RefKind.RefReadOnly ? CppLocation.Storage : CppLocation.None;
        }

        return CppLocation.Storage;
    }

    private static CppLocation ClassifyInvocation(InvocationExpressionSyntax invocation, SemanticModel model) =>
        model.GetSymbol(invocation) is IMethodSymbol { RefKind: RefKind.Ref or RefKind.RefReadOnly }
            ? CppLocation.Storage
            : CppLocation.None;

    // Whether the C++ expression that RoslynToCudaVisitor emits for an un-modified argument is an lvalue of the target
    // parameter type whose address '&' can take directly
    public static bool IsCppLValue(ExpressionSyntax expression, SemanticModel semanticModel) =>
        ClassifyLocation(expression, semanticModel) is CppLocation.Storage;

    /// <summary>
    /// Whether <paramref name="expression"/> is an auto-property (or a field/property chain on an auto-property) that G#
    /// emits as a C++ lvalue backing field, even though in C# it is a value returned by the property getter (#999).
    /// </summary>
    public static bool IsAutoPropertyOrMemberOfAutoProperty(ExpressionSyntax expression, SemanticModel semanticModel) =>
        ClassifyLocation(expression, semanticModel) is CppLocation.AutoPropertyStorage;
}
