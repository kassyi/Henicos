namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Decides whether C# removes a call, its arguments included: the call of a partial method without an implementing
/// declaration, or of a <c>[Conditional]</c> method none of whose symbols is defined in the file the call was
/// written in (<see cref="ConditionalCalls"/>). Every output asks here: the C++ transpiler, the lowering
/// (<c>RemovedCallEraser</c>), the managed rewriter, the suspension analysis and GSH0107.
/// </summary>
/// <remarks>
/// [WHY] Roslyn makes the same decision in <c>MethodSymbol.CallsAreOmitted</c> and drops the call in
/// <c>LocalRewriter.VisitUnusedExpression</c>; neither the operation tree nor the semantic model shows it. A
/// <c>[Conditional]</c> method returns <see langword="void"/> (CS0578), so a removed call stands only where a void
/// expression may: an expression statement, an initializer or incrementor of a <c>for</c>, the expression body of a
/// lambda, a local function or a method.
/// </remarks>
internal static class RemovedCalls
{
    /// <summary>
    /// Whether C# removes <paramref name="call"/>, a node of the tree of <paramref name="model"/> or a lowered copy of
    /// one; a call that lowering synthesized is never removed.
    /// </summary>
    public static bool IsRemoved(InvocationExpressionSyntax call, SemanticModel model) =>
        model.GetSymbol(call) is IMethodSymbol called &&
        GetUserTree(call, model) is { } tree &&
        IsRemoved(called, tree);

    /// <summary>Whether C# removes a call of <paramref name="called"/> written in <paramref name="tree"/>.</summary>
    public static bool IsRemoved(IMethodSymbol called, SyntaxTree tree)
    {
        var method = (called.ReducedFrom ?? called).OriginalDefinition;
        return method.IsPartialWithoutImplementation() || ConditionalCalls.IsRemoved(method, tree);
    }

    /// <summary>Whether <paramref name="expression"/> is a call that C# removes.</summary>
    public static bool IsRemovedCall(ExpressionSyntax? expression, SemanticModel model) =>
        expression is InvocationExpressionSyntax call && IsRemoved(call, model);

    /// <summary>
    /// The nodes under <paramref name="root"/> without the calls that C# removes and everything inside them, in
    /// document order; what runs of <paramref name="root"/>, for the analyses that follow calls.
    /// </summary>
    public static IEnumerable<SyntaxNode> DescendantNodesThatRun(SyntaxNode root, SemanticModel model)
    {
        foreach (var node in root.DescendantNodes(node => !IsRemovedCall(node as ExpressionSyntax, model)))
        {
            if (!IsRemovedCall(node as ExpressionSyntax, model))
            {
                yield return node;
            }
        }
    }

    /// <summary>
    /// The tree that <paramref name="node"/> was written in: the tree of <paramref name="model"/>, or the one that the
    /// original-span annotation of a lowered node names; <see langword="null"/> for a node that lowering synthesized.
    /// </summary>
    public static SyntaxTree? GetUserTree(SyntaxNode node, SemanticModel model) =>
        node.SyntaxTree == model.SyntaxTree
            ? node.SyntaxTree
            : model.GetOriginalNode(node)?.SyntaxTree;

    /// <summary>
    /// The symbols that a generated file of the managed output must <c>#define</c> so that C# keeps the calls that the
    /// declarations of <paramref name="type"/> keep: those that only a <c>#define</c> of the user's file defines.
    /// </summary>
    /// <remarks>
    /// [WHY] The generated file has the parse options of the compilation and none of the directives of the user's
    /// files, into which the bodies of the kernels and device functions are copied, and C# decides again there. The
    /// copies keep no call that C# removes (<c>RemovedCallEraser</c>, <see cref="ManagedExecutionRewriter"/>), so
    /// defining these symbols keeps exactly the calls that C# keeps in the user's files. A file without a
    /// <c>#define</c> adds none.
    /// </remarks>
    public static EquatableArray<string> GetSymbolsToDefine(INamedTypeSymbol type, Compilation compilation)
    {
        var symbols = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var tree = reference.SyntaxTree;
            if (!ConditionalCalls.HasDefineDirective(tree) || !compilation.ContainsSyntaxTree(tree))
            {
                continue;
            }

            reference.GetSyntax()
                .DescendantNodes()
                .AsStackEnumerable()
                .Select((Model: compilation.GetCachedSemanticModel(tree), Tree: tree), static (node, state) =>
                    node is InvocationExpressionSyntax call && state.Model.GetSymbol(call) is IMethodSymbol called
                        ? GetSymbolToDefine((called.ReducedFrom ?? called).OriginalDefinition, state.Tree)
                        : null
                ).OfType<string>()
                .AddTo(symbols);
        }

        return symbols.Count == 0
            ? default
            : ImmutableArray.CreateRange(symbols).AsEquatableArray();
    }

    // The condition of a kept call of method in tree that only a #define of the file defines, or null when the call is
    // removed, has no condition, or has one that the parse options define (the generated file defines it as well)
    private static string? GetSymbolToDefine(IMethodSymbol method, SyntaxTree tree)
    {
        var conditions = ConditionalCalls.GetConditions(method);
        string? defined = null;
        foreach (var condition in conditions)
        {
            if (ConditionalCalls.IsDefinedByOptions(condition, tree))
            {
                return null;
            }

            if (defined == null && ConditionalCalls.IsDefinedIn(condition, tree))
            {
                defined = condition;
            }
        }

        return defined;
    }
}
