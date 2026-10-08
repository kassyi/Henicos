namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

internal static class InitializationSyntaxExtractor
{
    public static bool TryGetArrayElementType(ITypeSymbol? typeSymbol, out ITypeSymbol? elemType, out bool isConst)
    {
        elemType = null;
        isConst = false;

        switch (typeSymbol)
        {
            case INamedTypeSymbol { TypeArguments.Length: 1, Name: "Span" or "ReadOnlySpan" } named:

                elemType = named.TypeArguments[0];

                isConst = named.Name == "ReadOnlySpan";

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

        return TryGetFromCollection(initializerValue, ref elementCount, ref initializerNode) ||
               TryGetFromStackAlloc(initializerValue, ref elementCount, ref sizeExpression, ref initializerNode) ||
               TryGetFromArrayCreation(initializerValue, ref elementCount, ref sizeExpression, ref initializerNode) ||
               TryGetFromInitializer(initializerValue, ref elementCount, ref initializerNode);
    }

    private static bool TryGetFromCollection(ExpressionSyntax expr, ref int? count, ref SyntaxNode? initNode)
    {
        if (expr is not CollectionExpressionSyntax collExpr)
        {
            return false;
        }

        count = collExpr.Elements.Count;
        initNode = collExpr;
        return true;
    }

    private static bool TryGetFromStackAlloc(ExpressionSyntax expr, ref int? count, ref ExpressionSyntax? sizeExpr,
        ref SyntaxNode? initNode)
    {
        switch (expr)
        {
            case StackAllocArrayCreationExpressionSyntax { Initializer: { } init }:
                count = init.Expressions.Count;
                initNode = init;
                return true;
            case StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax { RankSpecifiers.Count: > 0 } ats }:
                sizeExpr = ats.RankSpecifiers[0].Sizes.FirstOrDefault();
                return true;
            case ImplicitStackAllocArrayCreationExpressionSyntax implStackAlloc:
                count = implStackAlloc.Initializer.Expressions.Count;
                initNode = implStackAlloc.Initializer;
                return true;
        }

        return false;
    }

    private static bool TryGetFromArrayCreation(ExpressionSyntax expr, ref int? count, ref ExpressionSyntax? sizeExpr,
        ref SyntaxNode? initNode)
    {
        switch (expr)
        {
            case ArrayCreationExpressionSyntax { Initializer: { } init }:
                count = init.Expressions.Count;
                initNode = init;
                return true;
            case ArrayCreationExpressionSyntax { Type.RankSpecifiers.Count: > 0 } arrCreation:
                sizeExpr = arrCreation.Type.RankSpecifiers[0].Sizes.FirstOrDefault();
                return true;
            case ImplicitArrayCreationExpressionSyntax implicitArray:
                count = implicitArray.Initializer.Expressions.Count;
                initNode = implicitArray.Initializer;
                return true;
        }

        return false;
    }

    private static bool TryGetFromInitializer(ExpressionSyntax expr, ref int? count, ref SyntaxNode? initNode)
    {
        if (expr is not InitializerExpressionSyntax initExpr)
        {
            return false;
        }

        count = initExpr.Expressions.Count;
        initNode = initExpr;
        return true;
    }

    public static bool TryGetObjectCreationFirstArgument(ExpressionSyntax initializerValue, out ExpressionSyntax? argument)
    {
        argument = null;
        if (initializerValue is ObjectCreationExpressionSyntax objCreation)
        {
            argument = objCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            return true;
        }
        if (initializerValue is ImplicitObjectCreationExpressionSyntax implCreation)
        {
            argument = implCreation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            return true;
        }
        return false;
    }
}
