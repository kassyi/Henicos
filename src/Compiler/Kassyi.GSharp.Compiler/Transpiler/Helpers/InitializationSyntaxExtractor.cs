namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

internal static class InitializationSyntaxExtractor
{
    public static bool TryGetArrayElementType(ITypeSymbol? typeSymbol, out ITypeSymbol? elemType, out bool isConst)
    {
        elemType = null;
        isConst = false;

        switch (typeSymbol)
        {
            case INamedTypeSymbol { TypeArguments.Length: 1, Name: TypeConstants.SpanType or TypeConstants.ReadOnlySpanType } named
                when named.ContainingNamespace.IsSystemNamespace():

                elemType = named.TypeArguments[0];

                isConst = named.Name == TypeConstants.ReadOnlySpanType;

                return true;
            case IArrayTypeSymbol arrayType:
                elemType = arrayType.ElementType;
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetArrayInitialization(
        ExpressionSyntax initializerValue,
        out int? elementCount,
        out ExpressionSyntax? sizeExpression,
        out SyntaxNode? initializerNode)
    {
        elementCount = null;
        sizeExpression = null;
        initializerNode = null;

        var initExpr = initializerValue switch
        {
            StackAllocArrayCreationExpressionSyntax { Initializer: { } init } => init,
            ArrayCreationExpressionSyntax { Initializer: { } init } => init,
            ImplicitStackAllocArrayCreationExpressionSyntax implStack => implStack.Initializer,
            ImplicitArrayCreationExpressionSyntax implArray => implArray.Initializer,
            InitializerExpressionSyntax directInit => directInit,
            _ => null
        };

        if (initExpr != null)
        {
            elementCount = initExpr.Expressions.Count;
            initializerNode = initExpr;
            return true;
        }

        switch (initializerValue)
        {
            case CollectionExpressionSyntax collExpr:
                elementCount = collExpr.Elements.Count;
                initializerNode = collExpr;
                return true;
            case StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax { RankSpecifiers.Count: > 0 } ats }:
                sizeExpression = ats.RankSpecifiers[0].Sizes.FirstOrDefault();
                return true;
            case ArrayCreationExpressionSyntax { Type.RankSpecifiers.Count: > 0 } arrCreation:
                sizeExpression = arrCreation.Type.RankSpecifiers[0].Sizes.FirstOrDefault();
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetObjectCreationFirstArgument(ExpressionSyntax initializerValue, out ExpressionSyntax? argument)
    {
        if (initializerValue is BaseObjectCreationExpressionSyntax objCreation)
        {
            argument = objCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            return true;
        }

        argument = null;
        return false;
    }
}
