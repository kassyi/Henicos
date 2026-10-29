namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Annotates object creations of <c>SharedMemory&lt;T&gt;</c> with static site keys for runtime storage isolation.
/// </summary>
internal static class SharedMemorySiteKeyAnnotator
{
    /// <summary>
    /// Searches for <c>SharedMemory&lt;T&gt;</c> creations in <paramref name="node"/> and injects static site keys.
    /// </summary>
    /// <param name="node">The node whose creations get keys.</param>
    /// <param name="semanticModel">The model of the creations.</param>
    /// <param name="findOnly">
    /// Whether a dynamic creation only finds its region (the redeclaration at the start of a resume) instead of placing it.
    /// </param>
    public static TNode AddSharedMemorySiteKeys<TNode>(TNode node, SemanticModel semanticModel, bool findOnly = false)
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
                (original, rewritten) => AddSharedMemorySiteKey(original, rewritten, semanticModel, findOnly));
    }

    /// <summary>
    /// Computes and appends the static site key argument to a <c>SharedMemory&lt;T&gt;</c> object creation.
    /// </summary>
    /// <remarks>
    /// [WHY] <c>new SharedMemory&lt;T&gt;(n)</c> becomes <c>new SharedMemory&lt;T&gt;(n, key)</c>: the buffer belongs
    /// to the declaration, one per block, like a CUDA <c>__shared__</c> variable (see
    /// <see cref="SharedMemorySiteKey"/>). The key is computed from the original node, whose annotations map it to the
    /// source. A dynamic declaration (a count that is not a C# constant, as for the CUDA and native CPU placement)
    /// becomes <c>new SharedMemory&lt;T&gt;(n, key, "&lt;place&gt;")</c>: a region of the launch's
    /// <c>sharedMemBytes</c>, and the place names it in the error of a launch that does not give it enough (#544). With
    /// <paramref name="findOnly"/>, a dynamic declaration becomes <c>SharedMemory&lt;T&gt;.FindDynamic(key)</c>, which
    /// finds the region without placing it and without evaluating the count (a local that a resume has not declared yet).
    /// </remarks>
    public static ExpressionSyntax AddSharedMemorySiteKey(
        BaseObjectCreationExpressionSyntax original,
        BaseObjectCreationExpressionSyntax rewritten,
        SemanticModel semanticModel,
        bool findOnly = false)
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

        if (!DynamicSharedMemoryAnalysis.TryGetCount(original, semanticModel, out _, out var constantCount) ||
            constantCount != null)
        {
            return rewritten.WithArgumentList(SyntaxFactory.ArgumentList(arguments.Add(keyArgument)));
        }

        // The callers pass SharedMemory<T> creations only, whose type the model knows
        if (findOnly && semanticModel.GetTypeSymbol(original) is { } type)
        {
            // global::Kassyi.GSharp.Core.SharedMemory<T>.FindDynamic(<key>)
            var sharedMemoryType = SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    sharedMemoryType,
                    SyntaxFactory.IdentifierName(FindDynamicMethod)
                ),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(keyArgument))
            ).WithTriviaFrom(rewritten);
        }

        // new SharedMemory<T>(<n>, <key>, "<place>")
        var placeArgument = SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(SourcePlace.Format(semanticModel, original))));
        return rewritten.WithArgumentList(SyntaxFactory.ArgumentList(arguments.Add(keyArgument).Add(placeArgument)));
    }

    // SharedMemory<T>.FindDynamic in Core
    private const string FindDynamicMethod = "FindDynamic";
}
