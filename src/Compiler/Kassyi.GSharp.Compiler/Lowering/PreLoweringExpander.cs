namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands macro statements before the state-machine lowering passes run: a <c>Gpu.ForUnroll</c> or
/// <c>Gpu.GridStrideLoop</c> that contains phase barriers into a standard <c>for</c> loop
/// (<see cref="ForUnrollExpansion"/>, <see cref="GridStrideLoopExpansion"/>), and a <c>Gpu.SyncGrid(body)</c> into the
/// statements of its body with a <c>Gpu.SyncGrid()</c> in each gap (<see cref="SyncGridExpansion"/>).
/// </summary>
/// <remarks>
/// The state machine splits only at barriers that are statements of the method (as Roslyn lifts every <c>await</c> to
/// the statement level before its state machine rewriter runs), so a body that runs between grid syncs must become
/// statements first (#626).
/// </remarks>
public sealed class PreLoweringExpander(SemanticModel? semanticModel = null) : CSharpSyntaxRewriter
{
    private int _expansionCounter;

    /// <summary>Expands barrier-containing macro statements within <paramref name="block"/>.</summary>
    public static BlockSyntax Expand(BlockSyntax block, SemanticModel? semanticModel) =>
        new PreLoweringExpander(semanticModel).Expand(block);

    /// <summary>Expands barrier-containing macro statements within <paramref name="block"/>.</summary>
    public BlockSyntax Expand(BlockSyntax block) =>
        MacroExpansionSyntax.ContainsBarrier(block) ? (BlockSyntax)Visit(block) : block;

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) =>
        // Top-down expansion: nested macros in the expansion (in loops, ForUnroll bodies) are expanded in turn and
        // receive subsequent IDs
        ExpandMacro(node) is { } expanded
            ? Visit(expanded)
            : base.VisitExpressionStatement(node);

    // The statement that a macro statement expands into, or null when it stays as written
    private StatementSyntax? ExpandMacro(ExpressionStatementSyntax node)
    {
        if (SyncGridBody.TryGetBody(node, semanticModel, out var syncGridBody))
        {
            return SyncGridExpansion.Expand(node, syncGridBody, semanticModel, ref _expansionCounter);
        }

        if (node.Expression is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        if (TryClassifyMacro(invocation, GpuMacroKind.GridStrideLoop, out var gridStrideCall) &&
            GridStrideLoopExpansion.TryExpand(node, invocation, gridStrideCall, semanticModel, ref _expansionCounter)
                is { } gridStrideLoop)
        {
            return gridStrideLoop;
        }

        return TryClassifyMacro(invocation, GpuMacroKind.ForUnroll, out var forUnrollCall)
            ? ForUnrollExpansion.TryExpand(node, invocation, forUnrollCall, semanticModel, ref _expansionCounter)
            : null;
    }

    // The call as the semantic model resolves it; without one (the Tier 2 tests), by its spelling and argument count
    private bool TryClassifyMacro(InvocationExpressionSyntax invocation, GpuMacroKind kind, out GpuMacroCall call)
    {
        if (semanticModel != null)
        {
            call = semanticModel.GetSymbol(invocation) is IMethodSymbol methodSymbol
                ? GpuMacroClassifier.Classify(methodSymbol)
                : default;
            return call.Kind == kind;
        }

        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuType },
                Name.Identifier.ValueText: var memberName
            } => memberName,
            IdentifierNameSyntax { Identifier.ValueText: var identifierName } => identifierName,
            _ => null
        };
        call = (kind, name, invocation.ArgumentList.Arguments.Count) switch
        {
            (GpuMacroKind.ForUnroll, TypeConstants.GpuMacros.ForUnroll, 3) =>
                new GpuMacroCall(GpuMacroKind.ForUnroll, bodyOrdinal: 2),
            (GpuMacroKind.ForUnroll, TypeConstants.GpuMacros.ForUnroll, 4) =>
                new GpuMacroCall(GpuMacroKind.ForUnroll, hasContext: true, bodyOrdinal: 3),
            (GpuMacroKind.GridStrideLoop, TypeConstants.GpuMacros.GridStrideLoop, 2) =>
                new GpuMacroCall(GpuMacroKind.GridStrideLoop, bodyOrdinal: 1),
            (GpuMacroKind.GridStrideLoop, TypeConstants.GpuMacros.GridStrideLoop, 3) =>
                new GpuMacroCall(GpuMacroKind.GridStrideLoop, hasContext: true, bodyOrdinal: 2),
            _ => default
        };
        return call.Kind == kind;
    }
}
