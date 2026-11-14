namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What the CPU state machine may do with each <c>ref</c> local of a kernel or device function, from the liveness
/// behind GSH0039 (<see cref="CrossBarrierLiveness"/>): which ones it keeps as locals of their state, and which ones
/// GSH0039 reports.
/// </summary>
/// <param name="StaysLocal">
/// The <c>ref</c> locals of declaration statements that no state after a split reads and that are never rebound: their
/// declaration and every use lie in one state, so a barrier in their scope does not hoist them
/// (<c>HoistedVariableAnalysis</c>).
/// </param>
/// <param name="ReportedLive">
/// The <c>ref</c> locals that the analyzer reports as GSH0039: declared by a declaration statement and live right after
/// a split point.
/// </param>
/// <remarks>
/// The <c>ref</c> iteration variable of a <c>foreach</c> is in neither set: the liveness does not track it, and it never
/// stays a local, because its loop walks a span and a span temporary across a split does not build on the managed path
/// yet (#1005). The lowering hoists it when a barrier is in the loop, and the analyzer reports it as GSH0039 at the
/// variable (<c>CrossBarrierChecks.ReportRefForEachAroundSplit</c>), as for a ref local that is live across the split.
/// </remarks>
/// <remarks>
/// [WHY] A <c>ref</c> local cannot be a field of the managed frame class (CS9059), and the native frame would hold a
/// copy of its target. A ref local that the lowering still hoists is an error: GSH0039 when it is in
/// <see cref="ReportedLive"/>, otherwise GSH0003
/// (<see cref="GpuCodeConstraints.RefLocalHeldByCpuStateMachine"/>), which the generator reports (#1640).
/// </remarks>
internal sealed record RefLocalLiveness(HashSet<ISymbol> StaysLocal, HashSet<ISymbol> ReportedLive)
{
    /// <summary>
    /// The <c>ref</c> locals of <paramref name="body"/> sorted by what the CPU state machine may do with them, or
    /// <see langword="null"/> when the body declares none, when <paramref name="semanticModel"/> does not belong to the
    /// tree of <paramref name="body"/>, or when the body does not bind.
    /// </summary>
    /// <param name="body">The block or expression body of a method declaration.</param>
    /// <param name="semanticModel">The semantic model of the tree that declares the method.</param>
    public static RefLocalLiveness? Analyze(SyntaxNode body, SemanticModel? semanticModel)
    {
        if (semanticModel == null || semanticModel.SyntaxTree != body.SyntaxTree ||
            body.Parent is not MethodDeclarationSyntax method)
        {
            return null;
        }

        var refLocals = CollectRefLocals(body, method, semanticModel);
        if (refLocals.Count == 0 ||
            !new MethodFlowGraph(semanticModel, method).TryGetLiveness(out var graph, out var liveness))
        {
            return null;
        }

        var symbols = ImmutableArray.CreateBuilder<ISymbol>(refLocals.Count);
        refLocals
            .AsStackEnumerable()
            .Select(static local => local.Symbol)
            .AddTo(symbols);
        var live = CrossBarrierLiveness.FindLiveAcrossSplit(graph, liveness, symbols);
        var staysLocal = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var reportedLive = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (var i = 0; i < live.Length; i++)
        {
            if (live[i])
            {
                reportedLive.Add(refLocals[i].Symbol);
            }
            else if (!refLocals[i].IsRebound)
            {
                staysLocal.Add(refLocals[i].Symbol);
            }
        }

        return new RefLocalLiveness(staysLocal, reportedLive);
    }

    /// <summary>
    /// Whether <paramref name="type"/> declares a <c>ref</c> local: <c>ref T</c>, <c>ref readonly T</c>,
    /// <c>scoped ref T</c>.
    /// </summary>
    public static bool IsRefType(TypeSyntax type) =>
        type is RefTypeSyntax or ScopedTypeSyntax { Type: RefTypeSyntax };

    // The ref locals that the declaration statements of body declare (not in a lambda or local function), each with
    // whether it is ever rebound with `= ref`
    // [WHY] A ref local rebound after a split (`a = ref y;`) is not live there, but that state still names it, so it
    // cannot stay a local of the state that declares it
    private static ImmutableArray<RefLocal>.Builder CollectRefLocals(SyntaxNode body, MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        var declared = ImmutableArray.CreateBuilder<RefLocal>();
        var rebound = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } declarator
                    when IsRefType(declaration.Type) && IsDeclaredBy(declarator, method) &&
                         semanticModel.GetDeclaredSymbol<ILocalSymbol>(declarator) is { IsRef: true } local:
                    declared.Add(new RefLocal(local, IsRebound: false));
                    break;
                case AssignmentExpressionSyntax { Right: RefExpressionSyntax, Left: IdentifierNameSyntax target }
                    when semanticModel.GetSymbol(target) is ILocalSymbol reboundLocal:
                    rebound.Add(reboundLocal);
                    break;
            }
        }

        for (var i = 0; i < declared.Count; i++)
        {
            declared[i] = declared[i] with { IsRebound = rebound.Contains(declared[i].Symbol) };
        }

        return declared;
    }

    // Whether the nearest function around node is method itself, not a lambda or local function inside it
    private static bool IsDeclaredBy(SyntaxNode node, MethodDeclarationSyntax method) =>
        node.Ancestors()
            .AsStackEnumerable()
            .FirstOrDefault(static ancestor =>
                ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                    or BaseMethodDeclarationSyntax
            ) == method;

    // A ref local of the method: its symbol and whether it is rebound
    private readonly record struct RefLocal(ISymbol Symbol, bool IsRebound);
}
