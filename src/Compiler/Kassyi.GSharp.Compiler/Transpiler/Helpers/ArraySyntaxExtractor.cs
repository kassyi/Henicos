using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

internal static class ArraySyntaxExtractor
{
    public static bool TryGetArrayElementType(ITypeSymbol? typeSymbol, out ITypeSymbol elemType, out bool isConst)
    {
        elemType = null!;
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
        (var success, elementCount, sizeExpression, initializerNode) = initializerValue switch
        {
            CollectionExpressionSyntax collExpr
                => (true, collExpr.Elements.Count, null, collExpr),

            StackAllocArrayCreationExpressionSyntax { Initializer: { } init }
                => (true, init.Expressions.Count, null, init),

            StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax { RankSpecifiers.Count: > 0 } ats }
                => (true, null, ats.RankSpecifiers[0].Sizes.FirstOrDefault(), null),

            ImplicitStackAllocArrayCreationExpressionSyntax implStackAlloc
                => (true, implStackAlloc.Initializer.Expressions.Count, null, implStackAlloc.Initializer),

            ArrayCreationExpressionSyntax { Initializer: { } init }
                => (true, init.Expressions.Count, null, init),

            ArrayCreationExpressionSyntax { Type.RankSpecifiers.Count: > 0 } arrCreation
                => (true, null, arrCreation.Type.RankSpecifiers[0].Sizes.FirstOrDefault(), null),

            _ => (false, (int?)null, (ExpressionSyntax?)null, (SyntaxNode?)null)
        };

        return success;
    }
}
