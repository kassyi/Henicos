namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.StackArrays;

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
    /// Whether <paramref name="node"/> is a <c>stackalloc</c>, or a collection expression converted to a span, an array
    /// or a <c>[CollectionBuilder]</c> type (whose builder takes the elements as a span), that is not the initializer
    /// of a stack array local declaration.
    /// </summary>
    public static bool NeedsStorage(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            case StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax:
                return !IsStackArrayDeclarationInitializer(node, model);
            case CollectionExpressionSyntax:
                var target = ConvertedType(node, model);
                return !IsStackArrayDeclarationInitializer(node, model) &&
                       (InitializationSyntaxExtractor.TryGetArrayElementType(target, out _, out _) ||
                        CollectionBuilderMethod.Find(target) is not null);
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
        // A collection expression of a [CollectionBuilder] type is the span that its builder takes
        var builder = node is CollectionExpressionSyntax ? CollectionBuilderMethod.Find(target) : null;
        var storageType = builder is { Parameters: [var span] } ? span.Type : target;
        if (GetElementType(node, storageType, model) is not { } elementType ||
            !TryReadElements(node, model, out var length, out var elements, out problemNode, out problem))
        {
            return false;
        }

        // A C# array of length zero is not null, which the G# null representation cannot tell apart (Z5); an empty
        // span or a null pointer is what C# gives for the others
        if (length == 0 && target is IArrayTypeSymbol)
        {
            problem = GpuCodeConstraints.ZeroLengthArray;
            return false;
        }

        allocation = new StackAllocation(elementType, length, elements, target is IPointerTypeSymbol, builder);
        return true;
    }

    // The element type of the storage: the declared one of a stackalloc, the pointed-at type of a pointer, the element
    // of a span or an array
    private static ITypeSymbol? GetElementType(SyntaxNode node, ITypeSymbol? storageType, SemanticModel model) =>
        node is StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax arrayType }
            ? model.GetTypeSymbol(arrayType.ElementType)
            : storageType is IPointerTypeSymbol pointer
                ? pointer.PointedAtType
                : InitializationSyntaxExtractor.TryGetArrayElementType(storageType, out var element, out _)
                    ? element
                    : null;

    // The constant length and the initializer of the node; false with the node and the GSH0003 text to report for a
    // size that is not a constant or a spread element
    private static bool TryReadElements(SyntaxNode node, SemanticModel model, out int length,
        out SyntaxNode? elements, out SyntaxNode problemNode, out string problem)
    {
        length = 0;
        elements = null;
        problemNode = node;
        problem = node.Kind().ToString();
        switch (node)
        {
            case StackAllocArrayCreationExpressionSyntax { Initializer: { } initializer }:
                elements = initializer;
                length = initializer.Expressions.Count;
                return true;
            case StackAllocArrayCreationExpressionSyntax
            {
                Type: ArrayTypeSyntax { RankSpecifiers: [{ Sizes: [var size] }, ..] }
            }:
                if (model.GetSafeConstantValue(size) is not { HasValue: true, Value: int constant })
                {
                    problemNode = size;
                    problem = GpuCodeConstraints.DynamicArraySize;
                    return false;
                }

                length = constant;
                return true;
            case ImplicitStackAllocArrayCreationExpressionSyntax implicitStackAlloc:
                elements = implicitStackAlloc.Initializer;
                length = implicitStackAlloc.Initializer.Expressions.Count;
                return true;
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
                return true;
            default:
                return false;
        }
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
        if (!model.TryGetOriginalNode(node, out var original, out var originalModel) ||
            !model.Compilation.ContainsSyntaxTree(original.SyntaxTree))
        {
            return null;
        }

        var info = originalModel.GetTypeInfo(original);
        return info.ConvertedType ?? info.Type;
    }
}
