namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// A stack array that a <c>stackalloc</c> or a collection expression creates outside the declaration of a stack array
/// local: in an argument, in a pointer or <c>for</c> declaration, or nested in another expression.
/// </summary>
/// <param name="ElementType">The element type of the storage.</param>
/// <param name="Length">The constant element count.</param>
/// <param name="Elements">The initializer (<c>InitializerExpressionSyntax</c> or <c>CollectionExpressionSyntax</c>), or null.</param>
/// <param name="IsPointer">Whether the value is converted to a pointer (<c>int* p = stackalloc int[4]</c>).</param>
internal readonly record struct StackAllocation(
    ITypeSymbol ElementType,
    int Length,
    SyntaxNode? Elements,
    bool IsPointer);

/// <summary>
/// Classifies the <c>stackalloc</c> and collection expressions that need a storage of their own, which the statement
/// prelude declares (<see cref="StatementPrelude"/>) and the expression fills (<see cref="StackAllocEmitter"/>). The
/// declaration of a stack array local (<c>Span&lt;int&gt; s = stackalloc int[4]</c>) is its own storage and is not one of
/// them (<c>VariableDeclarationTranspilerRule</c>).
/// </summary>
internal static class StackArrayClassifier
{
    /// <summary>The GSH0003 text for a <c>stackalloc</c> that no statement prelude declared a storage for.</summary>
    public const string OutsideStatement = "stackalloc outside a statement";

    /// <summary>The GSH0003 text for a spread element (<c>[.. values]</c>), whose length is not a constant.</summary>
    public const string SpreadElement = "spread element in a collection expression";

    /// <summary>
    /// Whether <paramref name="node"/> is a <c>stackalloc</c>, or a collection expression converted to a span or an
    /// array, that is not the initializer of a stack array local declaration.
    /// </summary>
    public static bool NeedsStorage(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            case StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax:
                return !IsStackArrayDeclarationInitializer(node, model);
            case CollectionExpressionSyntax:
                return !IsStackArrayDeclarationInitializer(node, model) &&
                       InitializationSyntaxExtractor.TryGetArrayElementType(ConvertedType(node, model), out _, out _);
            default:
                return false;
        }
    }

    /// <summary>
    /// The element type, constant length and initializer of <paramref name="node"/>; when it cannot be a stack array, the
    /// node and the GSH0003 text to report (a size that is not a constant, a spread element, a zero-length array).
    /// </summary>
    public static bool TryClassify(SyntaxNode node, SemanticModel model, out StackAllocation allocation,
        out SyntaxNode problemNode, out string problem)
    {
        allocation = default;
        problemNode = node;
        problem = node.Kind().ToString();

        var target = ConvertedType(node, model);
        var isPointer = target is IPointerTypeSymbol;
        var elementType = node is StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax arrayType }
            ? model.GetTypeSymbol(arrayType.ElementType)
            : target is IPointerTypeSymbol pointer
                ? pointer.PointedAtType
                : InitializationSyntaxExtractor.TryGetArrayElementType(target, out var element, out _)
                    ? element
                    : null;
        if (elementType == null)
        {
            return false;
        }

        int length;
        SyntaxNode? elements;
        switch (node)
        {
            case StackAllocArrayCreationExpressionSyntax { Initializer: { } initializer }:
                elements = initializer;
                length = initializer.Expressions.Count;
                break;
            case StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax { RankSpecifiers: [{ Sizes: [var size] }, ..] } }:
                elements = null;
                if (model.GetSafeConstantValue(size) is not { HasValue: true, Value: int constant })
                {
                    problemNode = size;
                    problem = GpuCodeConstraints.DynamicArraySize;
                    return false;
                }

                length = constant;
                break;
            case ImplicitStackAllocArrayCreationExpressionSyntax implicitStackAlloc:
                elements = implicitStackAlloc.Initializer;
                length = implicitStackAlloc.Initializer.Expressions.Count;
                break;
            case CollectionExpressionSyntax collection:
                if (collection.Elements
                    .AsStackEnumerable()
                    .TryFirst(static e => e is not ExpressionElementSyntax, out var spread))
                {
                    problemNode = spread;
                    problem = SpreadElement;
                    return false;
                }

                elements = collection;
                length = collection.Elements.Count;
                break;
            default:
                return false;
        }

        // A C# array of length zero is not null, which the G# null representation cannot tell apart (Z5); an empty
        // span or a null pointer is what C# gives for the others
        if (length == 0 && target is IArrayTypeSymbol)
        {
            problem = GpuCodeConstraints.ZeroLengthArray;
            return false;
        }

        allocation = new StackAllocation(elementType, length, elements, isPointer);
        return true;
    }

    // The declaration path writes T name[N] for this initializer
    private static bool IsStackArrayDeclarationInitializer(SyntaxNode node, SemanticModel model) =>
        node.Parent is EqualsValueClauseSyntax
        {
            Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } declaration }
        } &&
        InitializationSyntaxExtractor.TryGetArrayElementType(model.GetTypeSymbol(declaration.Type), out _, out _);

    // The type that the value is converted to, looked up on the node of the user's source for a lowered node
    private static ITypeSymbol? ConvertedType(SyntaxNode node, SemanticModel model)
    {
        if (model.GetOriginalNode(node) is not { } original ||
            !model.Compilation.ContainsSyntaxTree(original.SyntaxTree))
        {
            return null;
        }

        var originalModel = original.SyntaxTree == model.SyntaxTree
            ? model
            : model.Compilation.GetCachedSemanticModel(original.SyntaxTree);
        var info = originalModel.GetTypeInfo(original);
        return info.ConvertedType ?? info.Type;
    }
}
