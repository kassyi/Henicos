namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The shared part of the lowering passes that flatten a statement holding a split point into the statement list around
/// it (<see cref="BranchRewriter"/>, <see cref="ForEachRewriter"/>, <see cref="LoopRewriter"/>): a block without a phase
/// barrier is left as it is, and a lowered statement that became a block is inlined into its parent block.
/// </summary>
public abstract class SplitPointBlockRewriter : CSharpSyntaxRewriter, IBlockLoweringPass
{
    /// <summary>Rewrites the statements of <paramref name="block"/> that hold a phase barrier.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) =>
        BarrierHelper.ContainsPhaseBarrier(block) ? (BlockSyntax)Visit(block) : block;

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(node.Statements.Count);

        foreach (var statement in node.Statements)
        {
            var visited = (StatementSyntax)Visit(statement);

            // [WHY] A statement other than a block (an if, a loop) only becomes a block when it holds a barrier and was
            // lowered. A block of the source is only inlined when it holds a barrier, so that its variable scope is kept.
            if (visited is BlockSyntax nestedBlock &&
                (statement is not BlockSyntax || BarrierHelper.ContainsPhaseBarrier(statement)))
            {
                newStatements.AddRange(nestedBlock.Statements);
            }
            else
            {
                newStatements.Add(visited);
            }
        }

        return SyntaxFactory.Block(newStatements);
    }

    /// <summary>Adds the statements of <paramref name="statement"/> when it is a block, or the statement itself.</summary>
    protected static void AddInlined(ImmutableArray<StatementSyntax>.Builder statements, StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            statements.AddRange(block.Statements);
        }
        else
        {
            statements.Add(statement);
        }
    }
}
