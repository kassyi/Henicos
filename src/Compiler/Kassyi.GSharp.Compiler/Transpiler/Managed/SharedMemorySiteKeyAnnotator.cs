namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Annotates object creations of <c>SharedMemory&lt;T&gt;</c> with static site keys for runtime storage isolation.
/// </summary>
internal static class SharedMemorySiteKeyAnnotator
{
    /// <summary>
    /// Searches for <c>SharedMemory&lt;T&gt;</c> creations in <paramref name="node"/> and injects static site keys.
    /// </summary>
    public static TNode AddSharedMemorySiteKeys<TNode>(TNode node, SemanticModel semanticModel)
        where TNode : SyntaxNode
    {
        var creations = ImmutableArray.CreateBuilder<BaseObjectCreationExpressionSyntax>();
        node.DescendantNodes()
            .AsStackEnumerable()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .AddIf(creations, semanticModel, static (creation, model) =>
                model.GetTypeSymbol(creation).IsSharedMemoryType()
            );

        return creations.Count == 0
            ? node
            : node.ReplaceNodes(creations,
                (original, rewritten) => AddSharedMemorySiteKey(original, rewritten, semanticModel));
    }

    /// <summary>
    /// Computes and appends the static site key argument to a <c>SharedMemory&lt;T&gt;</c> object creation.
    /// </summary>
    /// <remarks>
    /// [WHY] <c>new SharedMemory&lt;T&gt;(n)</c> becomes <c>new SharedMemory&lt;T&gt;(n, key)</c>: the buffer belongs
    /// to the declaration, one per block, like a CUDA <c>__shared__</c> variable (see
    /// <see cref="SharedMemorySiteKey"/>). The key is computed from the original node, whose annotations map it to the
    /// source.
    /// </remarks>
    public static BaseObjectCreationExpressionSyntax AddSharedMemorySiteKey(
        BaseObjectCreationExpressionSyntax original,
        BaseObjectCreationExpressionSyntax rewritten,
        SemanticModel semanticModel)
    {
        var key = SharedMemorySiteKey.Compute(semanticModel, original);
        var keyArgument = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(SharedMemorySiteKey.ToCSharpLiteral(key), key)));
        var arguments = rewritten.ArgumentList?.Arguments ?? default;
        if (arguments.Count == 0)
        {
            arguments = arguments.Add(SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1))));
        }

        return rewritten.WithArgumentList(SyntaxFactory.ArgumentList(arguments.Add(keyArgument)));
    }
}
