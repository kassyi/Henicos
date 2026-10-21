namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides zero-allocation extension methods for Roslyn <see cref="SemanticModel"/> queries.</summary>
internal static class SemanticModelExtensions
{
    private static bool IsInSameTree(SemanticModel semanticModel, SyntaxNode node) =>
        node.SyntaxTree == semanticModel.SyntaxTree;

    private static SyntaxNode? FindOriginalNode(SemanticModel semanticModel, SyntaxNode node)
    {
        if (!node.HasAnnotations(AstLoweringPipeline.OriginalSpanAnnotationKind))
        {
            return null;
        }

        foreach (var ann in node.GetAnnotations(AstLoweringPipeline.OriginalSpanAnnotationKind))
        {
            if (ann.Data is { } data)
            {
                var commaIdx = data.IndexOf(',');
                if (commaIdx > 0 &&
                    int.TryParse(data[..commaIdx], out var start) &&
                    int.TryParse(data[(commaIdx + 1)..], out var length))
                {
                    var span = new TextSpan(start, length);
                    return semanticModel.SyntaxTree.GetRoot().FindNode(span, getInnermostNodeForTie: true);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the node of the semantic model's tree that the specified node is, or was derived from by lowering, or null if
    /// unmapped.
    /// </summary>
    public static SyntaxNode? GetOriginalNode(this SemanticModel semanticModel, SyntaxNode node) =>
        IsInSameTree(semanticModel, node) ? node : FindOriginalNode(semanticModel, node);

    /// <summary>
    /// Gets the original location for the specified syntax node, or null if unmapped.
    /// </summary>
    public static Location? GetOriginalLocation(this SemanticModel semanticModel, SyntaxNode node)
    {
        var loc = GetLocationForNode(semanticModel, node);
        if (loc != null)
        {
            return loc;
        }

        if (node is VariableDeclaratorSyntax { Initializer.Value: { } initValue })
        {
            return GetLocationForNode(semanticModel, initValue);
        }

        return null;

        static Location? GetLocationForNode(SemanticModel model, SyntaxNode n)
        {
            if (IsInSameTree(model, n))
            {
                return n.GetLocation();
            }

            var orig = FindOriginalNode(model, n);
            return orig?.GetLocation();
        }
    }

    /// <summary>
    /// Gets the resolved symbol for the specified syntax node, or null if unresolvable.
    /// </summary>
    public static ISymbol? GetSymbol(this SemanticModel semanticModel, SyntaxNode node,
        CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
        }

        var original = FindOriginalNode(semanticModel, node);
        return original != null ? semanticModel.GetSymbolInfo(original, cancellationToken).Symbol : null;
    }

    /// <summary>
    /// Gets the resolved type symbol for the specified syntax node, or null if unresolvable.
    /// </summary>
    public static ITypeSymbol? GetTypeSymbol(this SemanticModel semanticModel, SyntaxNode node,
        CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return semanticModel.GetTypeInfo(node, cancellationToken).Type;
        }

        var original = FindOriginalNode(semanticModel, node);
        return original != null ? semanticModel.GetTypeInfo(original, cancellationToken).Type : null;
    }

    /// <summary>
    /// Gets the declared symbol for the specified syntax node cast to <typeparamref name="T"/>, or null if unresolvable or of different type.
    /// </summary>
    public static T? GetDeclaredSymbol<T>(this SemanticModel semanticModel, SyntaxNode node,
        CancellationToken cancellationToken = default) where T : class, ISymbol
    {
        if (IsInSameTree(semanticModel, node))
        {
            return semanticModel.GetDeclaredSymbol(node, cancellationToken) as T;
        }

        var original = FindOriginalNode(semanticModel, node);
        return original != null ? semanticModel.GetDeclaredSymbol(original, cancellationToken) as T : null;
    }

    /// <summary>
    /// Gets the constant value for the specified expression, or an empty Optional if unresolvable.
    /// </summary>
    public static Optional<object?> GetSafeConstantValue(this SemanticModel semanticModel, SyntaxNode node,
        CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return semanticModel.GetConstantValue(node, cancellationToken);
        }

        var original = FindOriginalNode(semanticModel, node);
        return original != null ? semanticModel.GetConstantValue(original, cancellationToken) : default;
    }

    /// <summary>
    /// Gets the operation for the specified expression, or null if unresolvable.
    /// </summary>
    public static IOperation? GetSafeOperation(this SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return semanticModel.GetOperation(node, cancellationToken);
        }

        var original = FindOriginalNode(semanticModel, node);
        return original != null ? semanticModel.GetOperation(original, cancellationToken) : null;
    }
}
