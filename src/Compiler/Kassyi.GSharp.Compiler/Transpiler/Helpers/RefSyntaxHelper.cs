namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

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

        context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TempAddress);
        AppendTempAddressTypeArgument(ref context, expression, semanticModel);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(expression);
        }
    }

    private static bool HasExplicitRefSyntax(ExpressionSyntax expression) =>
        expression.Parent is ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } or RefExpressionSyntax;

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

    // Whether the C++ expression that RoslynToCudaVisitor emits for an un-modified argument is an lvalue of the target
    // parameter type whose address '&' can take directly
    private static bool IsCppLValue(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var typeInfo = GetSafeTypeInfo(expression, semanticModel);
        if (typeInfo.Type != null &&
            typeInfo.ConvertedType != null &&
            !typeInfo.Type.IsEqualTo(typeInfo.ConvertedType))
        {
            return false;
        }

        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        switch (expression)
        {
            case DeclarationExpressionSyntax:
                return true;
            case IdentifierNameSyntax:
            {
                var symbol = semanticModel.GetSymbol(expression) ??
                             semanticModel.GetDeclaredSymbol<ISymbol>(expression);
                return IsLValueSymbol(symbol);
            }
            case MemberAccessExpressionSyntax memberAccess:
            {
                if (memberAccess.IsKind(SyntaxKind.PointerMemberAccessExpression))
                {
                    return true;
                }

                var symbol = semanticModel.GetSymbol(memberAccess) ??
                             semanticModel.GetSymbol(memberAccess.Name) ??
                             semanticModel.GetDeclaredSymbol<ISymbol>(memberAccess.Name);
                return IsLValueSymbol(symbol);
            }
            case ElementAccessExpressionSyntax:
            {
                if (semanticModel.GetSymbol(expression) is IPropertySymbol indexer)
                {
                    return indexer.RefKind is RefKind.Ref or RefKind.RefReadOnly;
                }

                return true;
            }
            case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PointerIndirectionExpression):
            case ConditionalExpressionSyntax { WhenTrue: RefExpressionSyntax }
                or ConditionalExpressionSyntax { WhenFalse: RefExpressionSyntax }:
                return true;
            case InvocationExpressionSyntax invocation:
                return semanticModel.GetSymbol(invocation) is IMethodSymbol
                {
                    RefKind: RefKind.Ref or RefKind.RefReadOnly
                };
            default:
                return false;
        }
    }

    private static bool IsLValueSymbol(ISymbol? symbol) =>
        symbol switch
        {
            ILocalSymbol or IParameterSymbol => true,
            IFieldSymbol { IsConst: false, IsStatic: false } => true,
            IPropertySymbol { RefKind: RefKind.Ref or RefKind.RefReadOnly } => true,
            IPropertySymbol { ContainingType: INamedTypeSymbol containingType } property =>
                GlobalCppNames.IsEmittedUserType(containingType) &&
                !StructAbiMembers.IsComputedProperty(property),
            _ => false
        };
}
