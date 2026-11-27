using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The edges that <see cref="SplicedFlowBuilder"/> draws between the nodes of the blocks of a graph, through a split
/// node where an edge crosses a gap of a <c>Gpu.SyncGrid</c> body.
/// </summary>
internal static class SplicedFlowEdges
{
    // The edges of a block; a branch with no destination (throw) leaves through the exit block. A branch whose
    // condition is always true or always false (the discard arm of a switch expression, whose other side throws
    // SwitchExpressionException) keeps only the side it takes. Any FinallyRegions crossed by a branch are built and
    // threaded between the source and destination so Dispose calls in using blocks are reachable in execution order.
    public static void ConnectBlock(SplicedFlowContext context, ImmutableArray<BasicBlock> blocks,
        SplicedFlowNode?[] starts, SplicedFlowNode?[] ends, int source, ControlFlowGraph graph, bool isRoot,
        ImmutableArray<SplicedFlowNode>.Builder? finallyExits = null)
    {
        var block = blocks[source];
        if (ends[source] is not { } sourceEnd)
        {
            return;
        }

        var exitBlock = blocks.Length - 1;
        var jumpsWhenTrue = block.ConditionKind == ControlFlowConditionKind.WhenTrue;
        var jumps = block.ConditionalSuccessor != null && GetConstantCondition(block) is { } constant
            ? constant == jumpsWhenTrue
            : (bool?)null;
        var fall = block.FallThroughSuccessor is { } fallThrough && jumps != true
            ? ConnectBranch(context, blocks, starts, ends, source, sourceEnd, fallThrough, exitBlock, graph, isRoot,
                finallyExits)
            : -1;
        if (block.ConditionalSuccessor is not { } conditional || jumps == false)
        {
            return;
        }

        var jump = ConnectBranch(context, blocks, starts, ends, source, sourceEnd, conditional, exitBlock, graph,
            isRoot, finallyExits);
        if (jumps == true)
        {
            return;
        }

        sourceEnd.WhenTrue = jumpsWhenTrue ? jump : fall;
        sourceEnd.WhenFalse = jumpsWhenTrue ? fall : jump;
    }

    // The value of a branch condition known without running it: a discard pattern always matches, a constant is itself
    public static bool? GetConstantCondition(BasicBlock block) =>
        block.BranchValue switch
        {
            IIsPatternOperation { Pattern: IDiscardPatternOperation } => true,
            { ConstantValue: { HasValue: true, Value: bool value } } => value,
            _ => null
        };

    private static int ConnectBranch(SplicedFlowContext context, ImmutableArray<BasicBlock> blocks,
        SplicedFlowNode?[] starts, SplicedFlowNode?[] ends, int source, SplicedFlowNode sourceEnd,
        ControlFlowBranch branch, int exitBlock, ControlFlowGraph graph, bool isRoot,
        ImmutableArray<SplicedFlowNode>.Builder? finallyExits)
    {
        if (finallyExits != null &&
            branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling &&
            branch.Destination == null)
        {
            finallyExits.Add(sourceEnd);
            return -1;
        }

        var target = branch.Destination?.Ordinal ?? exitBlock;
        if (branch.FinallyRegions.IsEmpty)
        {
            return ConnectEdge(context, blocks, starts, ends, source, sourceEnd, target);
        }

        var firstTarget = -1;
        var currentEnd = sourceEnd;
        var currentBlock = source;
        foreach (var finallyRegion in branch.FinallyRegions)
        {
            if (currentEnd == null)
            {
                break;
            }

            var (regionEntry, regionExit) = BuildFinallyRegion(context, finallyRegion, blocks, starts, ends, graph,
                isRoot);
            if (regionEntry == null)
            {
                continue;
            }

            var entryIndex = ConnectToNode(context, blocks, ends, currentBlock, currentEnd,
                finallyRegion.FirstBlockOrdinal, regionEntry);
            if (firstTarget < 0)
            {
                firstTarget = entryIndex;
            }

            currentEnd = regionExit;
            currentBlock = finallyRegion.LastBlockOrdinal;
        }

        if (currentEnd == null)
        {
            return firstTarget;
        }

        var finalIndex = ConnectEdge(context, blocks, starts, ends, currentBlock, currentEnd, target);
        return firstTarget >= 0 ? firstTarget : finalIndex;
    }

    private static (SplicedFlowNode? Entry, SplicedFlowNode? Exit) BuildFinallyRegion(SplicedFlowContext context,
        ControlFlowRegion finallyRegion, ImmutableArray<BasicBlock> blocks, SplicedFlowNode?[] starts,
        SplicedFlowNode?[] ends, ControlFlowGraph graph, bool isRoot)
    {
        var localStarts = (SplicedFlowNode?[])starts.Clone();
        var localEnds = (SplicedFlowNode?[])ends.Clone();
        for (var b = finallyRegion.FirstBlockOrdinal; b <= finallyRegion.LastBlockOrdinal; b++)
        {
            if (ReferenceEquals(SplicedFlowBuilder.FindEnclosingFinallyRegion(blocks[b]), finallyRegion))
            {
                SplicedFlowBuilder.BuildBlockNodes(context, blocks[b], graph, isRoot, out localStarts[b],
                    out localEnds[b]);
            }
        }

        var exits = ImmutableArray.CreateBuilder<SplicedFlowNode>();
        for (var b = finallyRegion.FirstBlockOrdinal; b <= finallyRegion.LastBlockOrdinal; b++)
        {
            if (localStarts[b] != null &&
                ReferenceEquals(SplicedFlowBuilder.FindEnclosingFinallyRegion(blocks[b]), finallyRegion))
            {
                ConnectBlock(context, blocks, localStarts, localEnds, b, graph, isRoot, exits);
            }
        }

        var regionEntry = localStarts[finallyRegion.FirstBlockOrdinal];
        if (regionEntry == null)
        {
            return (null, null);
        }

        if (exits.Count == 0)
        {
            return (regionEntry, null);
        }

        if (exits.Count == 1)
        {
            return (regionEntry, exits[0]);
        }

        var join = context.NewNode();
        foreach (var exit in exits)
        {
            SplicedFlowContext.Connect(exit, join);
        }

        return (regionEntry, join);
    }

    // An edge between two blocks; in a Gpu.SyncGrid body, through a split node when the edge crosses a gap. Returns the
    // node the edge leads to.
    private static int ConnectEdge(SplicedFlowContext context, ImmutableArray<BasicBlock> blocks,
        SplicedFlowNode?[] starts, SplicedFlowNode?[] ends, int source, SplicedFlowNode sourceEnd, int target) =>
        starts[target] is { } targetNode
            ? ConnectToNode(context, blocks, ends, source, sourceEnd, target, targetNode, starts)
            : -1;

    private static int ConnectToNode(SplicedFlowContext context, ImmutableArray<BasicBlock> blocks,
        SplicedFlowNode?[] ends, int source, SplicedFlowNode sourceEnd, int target, SplicedFlowNode targetNode,
        SplicedFlowNode?[]? starts = null)
    {
        if (context.Gaps is { } gaps &&
            (sourceEnd.Last ?? RangeBefore(blocks, ends, source, blocks.Length)) is { } before &&
            (targetNode.First ?? (starts != null ? RangeAfter(blocks, starts, target, blocks.Length) : null)) is
                { } after &&
            gaps.HasGap(before.Last, after.First))
        {
            var split = context.NewNode();
            context.Record(
                static (recorder, node) =>
                    recorder.OnSplit(new SplitPoint(SplitKind.Gap, null, null, null, null), node),
                split.Index
            );
            SplicedFlowContext.Connect(sourceEnd, split);
            SplicedFlowContext.Connect(split, targetNode);
            return split.Index;
        }

        SplicedFlowContext.Connect(sourceEnd, targetNode);
        return targetNode.Index;
    }

    // The element range of the last operation run before leaving block, looking back through empty blocks
    private static SyncGridElementRange? RangeBefore(ImmutableArray<BasicBlock> blocks, SplicedFlowNode?[] ends,
        int block, int depth)
    {
        if (ends[block]?.Last is { } last)
        {
            return last;
        }

        var predecessors = blocks[block].Predecessors;
        return depth > 0 && predecessors.Length == 1
            ? RangeBefore(blocks, ends, predecessors[0].Source.Ordinal, depth - 1)
            : null;
    }

    // The element range of the first operation run after entering block, looking ahead through empty blocks
    private static SyncGridElementRange? RangeAfter(ImmutableArray<BasicBlock> blocks, SplicedFlowNode?[] starts,
        int block, int depth)
    {
        if (starts[block]?.First is { } first)
        {
            return first;
        }

        var basicBlock = blocks[block];
        return depth > 0 && basicBlock.ConditionalSuccessor == null &&
               basicBlock.FallThroughSuccessor?.Destination is { } next
            ? RangeAfter(blocks, starts, next.Ordinal, depth - 1)
            : null;
    }
}
