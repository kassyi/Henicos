namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the <c>ref</c> locals of a kernel or device function that are not live right after any point where the CPU
/// dialects split its state machine (<see cref="CrossBarrierLiveness"/>, the liveness behind GSH0039).
/// </summary>
/// <remarks>
/// [WHY] A <c>ref</c> local cannot be a field of the managed frame class (CS9059: a ref field needs a ref struct). One
/// that no state after a split reads needs no field: its declaration and every use lie in one state, so the lowering
/// keeps it a local (<c>HoistedVariableAnalysis</c>). One that is live after a split is GSH0039 (#1640).
/// </remarks>
internal static class RefLocalLiveness
{
    /// <summary>
    /// The <c>ref</c> locals that <paramref name="body"/> declares directly (not in a lambda or a local function) and that
    /// are not live right after a split point, or <see langword="null"/> when there is none, when
    /// <paramref name="semanticModel"/> does not belong to the tree of <paramref name="body"/>, or when the body does
    /// not bind.
    /// </summary>
    /// <param name="body">The block or expression body of a method declaration.</param>
    /// <param name="semanticModel">The semantic model of the tree that declares the method.</param>
    public static HashSet<ISymbol>? FindNotLiveAcrossSplit(SyntaxNode body, SemanticModel? semanticModel)
    {
        if (semanticModel == null || semanticModel.SyntaxTree != body.SyntaxTree ||
            body.Parent is not MethodDeclarationSyntax method)
        {
            return null;
        }

        var declared = ImmutableArray.CreateBuilder<ISymbol>();
        var rebound = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in body.DescendantNodes())
        {
            if (node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } declarator &&
                IsRefType(declaration.Type) &&
                IsDeclaredBy(declarator, method) &&
                semanticModel.GetDeclaredSymbol<ILocalSymbol>(declarator) is { IsRef: true } local)
            {
                declared.Add(local);
            }
            else if (node is AssignmentExpressionSyntax { Right: RefExpressionSyntax, Left: IdentifierNameSyntax target } &&
                     semanticModel.GetSymbol(target) is ILocalSymbol reboundLocal)
            {
                rebound.Add(reboundLocal);
            }
        }

        // [WHY] A ref local rebound after a split (`a = ref y;`) is not live there, but that state still names it, so
        // it cannot stay a local of the state that declares it; such a local keeps being hoisted
        var refLocals = ImmutableArray.CreateBuilder<ISymbol>(declared.Count);
        declared
            .AsStackEnumerable()
            .Where(rebound, static (local, set) => !set.Contains(local))
            .AddTo(refLocals);
        if (refLocals.Count == 0 ||
            !new MethodFlowGraph(semanticModel, method).TryGetLiveness(out var graph, out var liveness))
        {
            return null;
        }

        var live = CrossBarrierLiveness.FindLiveAcrossSplit(graph, liveness, refLocals);
        var notLive = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (var i = 0; i < live.Length; i++)
        {
            if (!live[i])
            {
                notLive.Add(refLocals[i]);
            }
        }

        return notLive;
    }

    /// <summary>Whether <paramref name="type"/> declares a <c>ref</c> local: <c>ref T</c>, <c>ref readonly T</c>, <c>scoped ref T</c>.</summary>
    public static bool IsRefType(TypeSyntax type) =>
        type is RefTypeSyntax or ScopedTypeSyntax { Type: RefTypeSyntax };

    // Whether the nearest function around declarator is method itself, not a lambda or local function inside it
    private static bool IsDeclaredBy(SyntaxNode declarator, MethodDeclarationSyntax method) =>
        declarator.Ancestors()
            .AsStackEnumerable()
            .FirstOrDefault(static node =>
                node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax
            ) == method;
}
