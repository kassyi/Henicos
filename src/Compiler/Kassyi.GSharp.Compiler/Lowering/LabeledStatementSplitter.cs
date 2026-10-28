namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Detaches a label from a statement that holds a split point of the state machine: <c>L: s</c> becomes <c>L: ; s</c>.</summary>
/// <remarks>
/// [WHY] The lowering passes put the temporaries of a statement in front of it and split states at the statements of the
/// list. A label carrying a statement with a split point (a barrier, a warp shuffle, a call that suspends) would end up
/// after the hoisted temporaries, so a goto would not run the shuffle again, or it would hide the split point from
/// <see cref="StateMachineLowerer"/>, which then drops the barrier. A C# labeled statement is never an embedded statement,
/// so it is always in a statement list and can be split there. The label still names the same place, so the C# meaning
/// is kept. A label without a split point under it is left as it is.
/// </remarks>
internal static class LabeledStatementSplitter
{
    /// <summary>Splits every labeled statement of <paramref name="body"/> whose statement holds a split point.</summary>
    public static BlockSyntax Split(BlockSyntax body) => (BlockSyntax)new Rewriter().Visit(body);

    private static bool ContainsSplitPoint(SyntaxNode node) =>
        node.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && BarrierHelper.IsPhaseBarrier(inv));

    private static bool NeedsSplit(StatementSyntax statement) =>
        statement is LabeledStatementSyntax labeled && ContainsSplitPoint(labeled.Statement);

    // L1: L2: <stmt>  ->  L1: ; L2: ; <stmt>
    private static SyntaxList<StatementSyntax> SplitList(SyntaxList<StatementSyntax> statements)
    {
        if (!statements.AsStackEnumerable().Any(NeedsSplit))
        {
            return statements;
        }

        var split = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count + 1);
        foreach (var statement in statements)
        {
            var current = statement;
            while (NeedsSplit(current))
            {
                // The label keeps its annotations (the original span); the new empty statement needs none
                var labeled = (LabeledStatementSyntax)current;
                split.Add(labeled.WithStatement(SyntaxFactory.EmptyStatement()));
                current = labeled.Statement;
            }

            split.Add(current);
        }

        return SyntaxFactory.List(split);
    }

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            var visited = (BlockSyntax?)base.VisitBlock(node) ?? node;
            return visited.WithStatements(SplitList(visited.Statements));
        }

        public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
        {
            var visited = (SwitchSectionSyntax?)base.VisitSwitchSection(node) ?? node;
            return visited.WithStatements(SplitList(visited.Statements));
        }
    }
}
