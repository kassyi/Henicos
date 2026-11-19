using Kassyi.GSharp.Compiler.Instantiation;

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

    /// <summary>
    /// Returns <paramref name="defaultModel"/> if its syntax tree is <paramref name="targetTree"/>, or the cached
    /// semantic model for <paramref name="targetTree"/> in the same compilation.
    /// </summary>
    internal static SemanticModel GetSemanticModelForTree(SemanticModel defaultModel, SyntaxTree targetTree) =>
        defaultModel.SyntaxTree == targetTree
            ? defaultModel
            : defaultModel.Compilation.GetCachedSemanticModel(targetTree);

    /// <summary>
    /// The semantic model of <paramref name="tree"/>, a tree of <paramref name="compilation"/>, shared by every caller
    /// for the life of the compilation (a model caches its bindings, so creating one per query binds everything again).
    /// </summary>
    public static SemanticModel GetCachedSemanticModel(this Compilation compilation, SyntaxTree tree)
    {
        var models = s_semanticModels.GetValue(
            compilation,
            static _ => new ConcurrentDictionary<SyntaxTree, SemanticModel>());
        if (!models.TryGetValue(tree, out var model))
        {
            model = compilation.GetSemanticModel(tree);
            models.TryAdd(tree, model);
        }

        return model;
    }

    private static bool IsInSameTree(SemanticModel semanticModel, SyntaxNode node) =>
        node.SyntaxTree == semanticModel.SyntaxTree;

    private static bool TryFindOriginalNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        out SemanticModel targetModel,
        [NotNullWhen(true)] out SyntaxNode? originalNode) =>
        TryFindAnnotatedNode(
            semanticModel,
            node,
            LoweringAnnotations.OriginalSpanAnnotationKind,
            out targetModel,
            out originalNode);

    private static bool TryFindAnnotatedNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string annotationKind,
        out SemanticModel targetModel,
        [NotNullWhen(true)] out SyntaxNode? originalNode)
    {
        targetModel = semanticModel;
        originalNode = null;
        if (!node.HasAnnotations(annotationKind))
        {
            return false;
        }

        foreach (var ann in node.GetAnnotations(annotationKind))
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

            originalNode = FindNodeAt(targetTree, new TextSpan(start, length));
            return true;
        }

        return false;
    }

    // The innermost node at span. An element binding ([i] of a?[i]) and its argument list share the span; the
    // expression is the binding, which a rewrite of the binding (the element access of a null-conditional access)
    // stands for
    private static SyntaxNode FindNodeAt(SyntaxTree tree, TextSpan span)
    {
        var node = tree.GetRoot().FindNode(span, getInnermostNodeForTie: true);
        return node is BracketedArgumentListSyntax { Parent: ElementBindingExpressionSyntax binding } &&
               binding.Span == span
            ? binding
            : node;
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
    /// The node of the compilation tree that <paramref name="node"/> is, or was derived from by lowering
    /// (<see cref="GetOriginalNode"/>), with the semantic model of that node's tree; false when it is unmapped.
    /// </summary>
    public static bool TryGetOriginalNode(this SemanticModel semanticModel, SyntaxNode node,
        [NotNullWhen(true)] out SyntaxNode? originalNode, out SemanticModel originalModel)
    {
        originalNode = semanticModel.GetOriginalNode(node);
        originalModel = originalNode is null
            ? semanticModel
            : GetSemanticModelForTree(semanticModel, originalNode.SyntaxTree);
        return originalNode is not null;
    }

    /// <summary>
    /// The element of the collection initializer in the user's source that <paramref name="element"/> (an element of a
    /// collection or complex element initializer, also of a lowered tree) stands for, with the semantic model of its
    /// tree; false when the initializer is unmapped.
    /// </summary>
    /// <remarks>
    /// [WHY] Lowering may replace the element itself with a temporary that maps to no node of the source: a call
    /// hoisted out of it (WarpInstructionRewriter), a ?: or &amp;&amp; lowered into statements
    /// (ConditionalEvaluationRewriter). The initializer around it keeps its original span and its elements one for one,
    /// so the element is found by its index there, as CallerInfoArgument reads an argument through the call.
    /// </remarks>
    public static bool TryGetOriginalInitializerElement(this SemanticModel semanticModel, ExpressionSyntax element,
        [NotNullWhen(true)] out ExpressionSyntax? originalElement, out SemanticModel originalModel)
    {
        originalElement = null;
        if (element.Parent is not InitializerExpressionSyntax initializer ||
            !semanticModel.TryGetOriginalNode(initializer, out var original, out originalModel) ||
            original is not InitializerExpressionSyntax { Expressions: var originalElements } ||
            originalElements.Count != initializer.Expressions.Count)
        {
            originalModel = semanticModel;
            return false;
        }

        originalElement = originalElements[initializer.Expressions.IndexOf(element)];
        return true;
    }

    /// <summary>
    /// The <c>Add</c> method that C# calls for <paramref name="element"/>, an element of a collection initializer, also
    /// for an element of a lowered tree (<see cref="TryGetOriginalInitializerElement"/>); null when it binds to none.
    /// </summary>
    public static IMethodSymbol? GetCollectionInitializerAdd(this SemanticModel semanticModel,
        ExpressionSyntax element) =>
        semanticModel.TryGetOriginalInitializerElement(element, out var original, out var originalModel)
            ? originalModel.GetCollectionInitializerSymbolInfo(original).Symbol as IMethodSymbol
            : null;

    /// <summary>
    /// Gets the original location for the specified syntax node, or null if unmapped. The location of a node of a closed
    /// copy of a generic declaration is that of the declaration node it came from (<see cref="GenericCopyOrigins"/>).
    /// </summary>
    public static Location? GetOriginalLocation(this SemanticModel semanticModel, SyntaxNode node) =>
        FindCompilationNode(semanticModel, node) is { } compilationNode ? GetUserLocation(compilationNode) : null;

    // The node of the compilation tree that <node> is or was derived from by lowering, or that its initializer value is
    // for a declarator; null when unmapped
    private static SyntaxNode? FindCompilationNode(SemanticModel semanticModel, SyntaxNode node)
    {
        if (FindForNode(semanticModel, node) is { } found)
        {
            return found;
        }

        return node is VariableDeclaratorSyntax { Initializer.Value: { } initValue }
            ? FindForNode(semanticModel, initValue)
            : null;

        static SyntaxNode? FindForNode(SemanticModel model, SyntaxNode n)
        {
            if (IsInSameTree(model, n))
            {
                return n;
            }

            return TryFindOriginalNode(model, n, out _, out var orig) ? orig : null;
        }
    }

    // The location of <node>, a node of a compilation tree, in the user's source: a node of a closed copy goes back to
    // the generic declaration
    private static Location GetUserLocation(SyntaxNode node) =>
        GenericCopyOrigins.TryGetLocation(node, out var location) ? location : node.GetLocation();

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
    /// <para>
    /// A node of a closed copy of a generic declaration, also one that lowering derived from it, is reported at the
    /// declaration node it came from in the user's compilation (<see cref="GenericCopyOrigins"/>): the copy is in a
    /// derived compilation, whose trees a generator of the user's compilation cannot report in either.
    /// </para>
    /// </remarks>
    public static Location GetReportLocation(this SemanticModel semanticModel, SyntaxNode node)
    {
        if (semanticModel.Compilation.ContainsSyntaxTree(node.SyntaxTree))
        {
            return GetUserLocation(node);
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
    /// Whether the locals at <paramref name="node"/> start zeroed: <see cref="SymbolExtensions.AreLocalsZeroed"/> of the
    /// member around the node's position in the user's source.
    /// </summary>
    /// <remarks>
    /// A node of a lowered tree is located through the original-span annotation of the node or of its nearest mapped
    /// ancestor, so a body that lowering copied from another method follows the attributes of that method (Roslyn's
    /// synthesized methods inherit the flag from their owner the same way). A node with no location counts as zeroed.
    /// </remarks>
    public static bool AreLocalsZeroedAt(this SemanticModel semanticModel, SyntaxNode node)
    {
        // The node in the compilation, not its place in the user's source: a closed copy is a member of the derived
        // compilation, and its own declaration carries the attributes of the generic one
        for (var current = node; current != null; current = current.Parent)
        {
            if (FindCompilationNode(semanticModel, current) is { SyntaxTree: var tree } found &&
                semanticModel.Compilation.ContainsSyntaxTree(tree))
            {
                return GetSemanticModelForTree(semanticModel, tree)
                    .GetEnclosingSymbol(found.SpanStart)
                    .AreLocalsZeroed();
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the resolved symbol for the specified syntax node, or null if unresolvable.
    /// </summary>
    public static ISymbol? GetSymbol(this SemanticModel semanticModel, SyntaxNode node,
        CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return ResolveSymbolOrDeclared(semanticModel, node, cancellationToken);
        }

        return TryFindOriginalNode(semanticModel, node, out var targetModel, out var original)
            ? ResolveSymbolOrDeclared(targetModel, original, cancellationToken)
            : null;

        static ISymbol? ResolveSymbolOrDeclared(
            SemanticModel model,
            SyntaxNode target,
            CancellationToken ct) =>
            model.GetSymbolInfo(target, ct).Symbol ??
            (target is SingleVariableDesignationSyntax or VariableDeclaratorSyntax
                ? model.GetDeclaredSymbol(target, ct)
                : null);
    }

    /// <summary>
    /// Whether <paramref name="expression"/> is a discard: <c>_</c> where no variable of that name is in scope, or a
    /// declaration of one (<c>var _</c>, <c>int _</c>) in a deconstruction or an <c>out</c> argument.
    /// </summary>
    public static bool IsDiscard(this SemanticModel semanticModel, ExpressionSyntax expression) =>
        expression is DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax } ||
        (expression is IdentifierNameSyntax { Identifier.ValueText: "_" } &&
         semanticModel.GetSymbol(expression) is IDiscardSymbol);

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

        if (TryFindOriginalNode(semanticModel, node, out var targetModel, out var original))
        {
            return targetModel.GetTypeInfo(original, cancellationToken).Type;
        }

        if (TryFindAnnotatedNode(
                semanticModel,
                node,
                LoweringAnnotations.SpilledTypeSpanAnnotationKind,
                out targetModel,
                out var spilledOriginal))
        {
            var typeInfo = targetModel.GetTypeInfo(spilledOriginal, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        return null;
    }

    /// <summary>
    /// Gets the type the specified expression converts to in its context (the parameter type for an argument), or null if
    /// unresolvable.
    /// </summary>
    public static ITypeSymbol? GetConvertedTypeSymbol(this SemanticModel semanticModel, SyntaxNode node,
        CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, node))
        {
            return semanticModel.GetTypeInfo(node, cancellationToken).ConvertedType;
        }

        if (TryFindOriginalNode(semanticModel, node, out var targetModel, out var original))
        {
            return targetModel.GetTypeInfo(original, cancellationToken).ConvertedType;
        }

        if (TryFindAnnotatedNode(
                semanticModel,
                node,
                LoweringAnnotations.SpilledTypeSpanAnnotationKind,
                out targetModel,
                out var spilledOriginal))
        {
            var typeInfo = targetModel.GetTypeInfo(spilledOriginal, cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }

        return null;
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
    /// Gets the conversion that converts <paramref name="expression"/> (a node of the model's tree, or of a lowered tree
    /// mapped to it), or the identity conversion when unresolvable.
    /// </summary>
    public static Conversion GetSafeConversion(this SemanticModel semanticModel, ExpressionSyntax expression,
        CancellationToken cancellationToken = default)
    {
        if (IsInSameTree(semanticModel, expression))
        {
            return semanticModel.GetConversion(expression, cancellationToken);
        }

        return TryFindOriginalNode(semanticModel, expression, out var targetModel, out var original)
            ? targetModel.GetConversion(original, cancellationToken)
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
