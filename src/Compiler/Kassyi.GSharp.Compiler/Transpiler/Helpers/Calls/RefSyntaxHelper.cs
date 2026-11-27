using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

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
        if (DiscardOutArgument.TryAppend(expression, ref context))
        {
            return;
        }

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

        // An auto-property (emitted as its backing field) or a conditional / assignment expression (or member chain on one)
        // is a C++ lvalue even though it is a C# value, so copy it into a temporary rather than aliasing it (#999).
        AppendTempAddress(ref context, expression, semanticModel, NeedsTemporaryValueCopy(expression, semanticModel));
    }

    /// <summary>
    /// Emits <c>__gsharp_temp_address&lt;T&gt;(...)</c> for <paramref name="expression"/>, copying through
    /// <c>static_cast&lt;T&gt;(...)</c> when <paramref name="forceValueCopy"/> is <see langword="true"/> so a C++
    /// lvalue does not alias underlying storage.
    /// </summary>
    public static void AppendTempAddress(ref TranspileContext context, ExpressionSyntax expression,
        SemanticModel semanticModel, bool forceValueCopy)
    {
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
        semanticModel.TryGetOriginalNode(expression, out var original, out var originalModel)
            ? originalModel.GetTypeInfo(original)
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
        return IsConverted(expression, model) ? CppLocation.None : ClassifyStorage(expression, model);
    }

    /// <summary>
    /// <see cref="ClassifyLocation"/> of <paramref name="expression"/> itself, whatever C# converts its value to: the
    /// receiver of a <c>GetPinnableReference</c> that a <c>fixed</c> statement calls, whose value C# converts to the
    /// pointer.
    /// </summary>
    public static CppLocation ClassifyStorage(ExpressionSyntax expression, SemanticModel model)
    {
        return expression.UnwrapParentheses() switch
        {
            DeclarationExpressionSyntax => CppLocation.Storage,
            ThisExpressionSyntax => CppLocation.Storage,
            // [WHY] A condition temporary declared by lowering has no symbol; in C++ it is a local variable or context
            // field (not a temporary rvalue), so passing it by reference takes its address directly.
            IdentifierNameSyntax identifier when model.GetSymbol(identifier) is null &&
                                                 LoweringTempNames.IsConditionTemp(identifier.Identifier.ValueText) =>
                CppLocation.Storage,
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

        if (access.Expression.UnwrapParentheses() is ThisExpressionSyntax ||
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
            ILocalSymbol { IsConst: false } or IParameterSymbol => CppLocation.Storage,
            IFieldSymbol { IsConst: false, IsStatic: false } => CppLocation.Storage,
            IPropertySymbol { RefKind: RefKind.Ref or RefKind.RefReadOnly } => CppLocation.Storage,
            IPropertySymbol { ContainingType: INamedTypeSymbol containingType } property
                when GlobalCppNames.IsEmittedUserType(containingType) &&
                     !StructAbiMembers.HasAccessorFunctions(property) =>
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

    /// <summary>
    /// Whether <paramref name="expression"/>, a C# value/rvalue, emits as a C++ lvalue (an auto-property or member
    /// chain on one, a non-variable inline array element access, or a conditional or assignment expression or member
    /// chain on one) and must therefore be copied into a temporary before taking its address.
    /// </summary>
    public static bool NeedsTemporaryValueCopy(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (IsAutoPropertyOrMemberOfAutoProperty(expression, semanticModel))
        {
            return true;
        }

        var current = UnwrapParensAndChecked(expression);
        while (current is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax)
        {
            if (current is MemberAccessExpressionSyntax access)
            {
                current = UnwrapParensAndChecked(access.Expression);
            }
            else if (current is ElementAccessExpressionSyntax element)
            {
                if (IsAutoPropertyOrMemberOfAutoProperty(element.Expression, semanticModel))
                {
                    return true;
                }

                // [WHY] An element of a non-variable [InlineArray] (such as MakeBuf(10)[0]) calls C++ operator[]
                // returning S&, which would dangle under auto&& and fail T&& deduction in __gsharp_temp_mutable_address.
                if (semanticModel.GetTypeSymbol(element.Expression) is { } receiverType &&
                    receiverType.HasAttribute(KnownAttributes.InlineArray))
                {
                    var isInlineArrayVar = semanticModel.GetSafeOperation(element) is IInlineArrayAccessOperation inlineArray
                        ? inlineArray.Instance.IsVariable()
                        : ClassifyLocation(element.Expression, semanticModel) is CppLocation.Storage;
                    if (!isInlineArrayVar)
                    {
                        return true;
                    }
                }

                current = UnwrapParensAndChecked(element.Expression);
            }
        }

        return current is ConditionalExpressionSyntax or AssignmentExpressionSyntax;
    }

    private static ExpressionSyntax UnwrapParensAndChecked(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = expression.UnwrapParentheses();
            if (expression is CheckedExpressionSyntax checkedExpression)
            {
                expression = checkedExpression.Expression;
                continue;
            }

            return expression;
        }
    }
}
