namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides zero-allocation extension methods for Roslyn <see cref="SemanticModel"/> queries.</summary>
internal static class SemanticModelExtensions
{
    private static readonly ConditionalWeakTable<Compilation, SyntaxTree[]> s_syntaxTrees = new();
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<SyntaxTree, SemanticModel>> s_semanticModels = new();

    internal static SyntaxTree[] GetSyntaxTrees(Compilation compilation) =>
        s_syntaxTrees.GetValue(compilation, static c => [.. c.SyntaxTrees]);

    internal static int GetSyntaxTreeOrdinal(Compilation? compilation, SyntaxTree? tree)
    {
        if (compilation == null || tree == null)
        {
            return -1;
        }

        return Array.IndexOf(GetSyntaxTrees(compilation), tree);
    }

    private static SemanticModel GetSemanticModelForTree(SemanticModel defaultModel, SyntaxTree targetTree)
    {
        if (defaultModel.SyntaxTree == targetTree)
        {
            return defaultModel;
        }

        var models = s_semanticModels.GetValue(
            defaultModel.Compilation,
            static _ => new ConcurrentDictionary<SyntaxTree, SemanticModel>());
        if (!models.TryGetValue(targetTree, out var targetModel))
        {
            targetModel = defaultModel.Compilation.GetSemanticModel(targetTree);
            models.TryAdd(targetTree, targetModel);
        }

        return targetModel;
    }

    private static bool IsInSameTree(SemanticModel semanticModel, SyntaxNode node) =>
        node.SyntaxTree == semanticModel.SyntaxTree;

    private static bool TryFindOriginalNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        out SemanticModel targetModel,
        [NotNullWhen(true)] out SyntaxNode? originalNode)
    {
        targetModel = semanticModel;
        originalNode = null;
        if (!node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            return false;
        }

        foreach (var ann in node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            if (!LoweringAnnotations.TryParseOriginalSpanData(
                    ann.Data,
                    out var start,
                    out var length,
                    out var treeOrdinal,
                    out _))
            {
                continue;
            }

            SyntaxTree targetTree;
            if (treeOrdinal < 0)
            {
                targetTree = semanticModel.SyntaxTree;
            }
            else
            {
                var trees = GetSyntaxTrees(semanticModel.Compilation);
                if ((uint)treeOrdinal >= (uint)trees.Length)
                {
                    continue;
                }

                targetTree = trees[treeOrdinal];
                targetModel = GetSemanticModelForTree(semanticModel, targetTree);
            }

            var span = new TextSpan(start, length);
            originalNode = targetTree.GetRoot().FindNode(span, getInnermostNodeForTie: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the node of the compilation tree that the specified node is, or was derived from by lowering, or null if
    /// unmapped.
    /// </summary>
    public static SyntaxNode? GetOriginalNode(this SemanticModel semanticModel, SyntaxNode node)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return node;
        }

        return TryFindOriginalNode(semanticModel, node, out _, out var originalNode) ? originalNode : null;
    }

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

            return TryFindOriginalNode(model, n, out _, out var orig) ? orig.GetLocation() : null;
        }
    }

    /// <summary>
    /// Gets the location at which a diagnostic about <paramref name="node"/> is reported: its own location when it is in
    /// the compilation, otherwise the original location of it or of its nearest mapped ancestor (lowering synthesizes
    /// nodes without one), or <see cref="Location.None"/>.
    /// </summary>
    /// <remarks>
    /// A source generator may not report a location in a tree outside the compilation, which is where every node of a
    /// lowered body is. The mapping relies on the original-span annotation that lowering puts on every node it copies
    /// (<see cref="LoweringAnnotations.OriginalSpanAnnotationKind"/>). A node with no annotated ancestor is reported
    /// without a location, and the generator treats all location-less diagnostics with the same ID as one when it drops
    /// the C++ diagnostics that the CUDA run already reported, so such a C++-only diagnostic can be lost.
    /// </remarks>
    public static Location GetReportLocation(this SemanticModel semanticModel, SyntaxNode node)
    {
        if (semanticModel.Compilation.ContainsSyntaxTree(node.SyntaxTree))
        {
            return node.GetLocation();
        }

        for (var current = node; current != null; current = current.Parent)
        {
            if (semanticModel.GetOriginalLocation(current) is { } location)
            {
                return location;
            }
        }

        return Location.None;
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

        return TryFindOriginalNode(semanticModel, node, out var targetModel, out var original)
            ? targetModel.GetSymbolInfo(original, cancellationToken).Symbol
            : null;
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

        return TryFindOriginalNode(semanticModel, node, out var targetModel, out var original)
            ? targetModel.GetTypeInfo(original, cancellationToken).Type
            : null;
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

        return TryFindOriginalNode(semanticModel, node, out var targetModel, out var original)
            ? targetModel.GetDeclaredSymbol(original, cancellationToken) as T
            : null;
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

        return TryFindOriginalNode(semanticModel, node, out var targetModel, out var original)
            ? targetModel.GetConstantValue(original, cancellationToken)
            : default;
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

        return TryFindOriginalNode(semanticModel, node, out var targetModel, out var original)
            ? targetModel.GetOperation(original, cancellationToken)
            : null;
    }
}
