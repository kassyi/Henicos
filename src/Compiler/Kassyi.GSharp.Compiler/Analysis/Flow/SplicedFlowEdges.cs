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
    // SwitchExpressionException) keeps only the side it takes
    public static void ConnectBlock(SplicedFlowContext context, ImmutableArray<BasicBlock> blocks,
        SplicedFlowNode[] starts, SplicedFlowNode[] ends, int source)
    {
        var block = blocks[source];
        var exitBlock = blocks.Length - 1;
        var jumpsWhenTrue = block.ConditionKind == ControlFlowConditionKind.WhenTrue;
        var jumps = block.ConditionalSuccessor != null && GetConstantCondition(block) is { } constant
            ? constant == jumpsWhenTrue
            : (bool?)null;
        var fall = block.FallThroughSuccessor is { } fallThrough && jumps != true
            ? ConnectEdge(context, blocks, starts, ends, source, fallThrough.Destination?.Ordinal ?? exitBlock)
            : -1;
        if (block.ConditionalSuccessor is not { } conditional || jumps == false)
        {
            return;
        }

        var jump = ConnectEdge(context, blocks, starts, ends, source, conditional.Destination?.Ordinal ?? exitBlock);
        if (jumps == true)
        {
            return;
        }

        ends[source].WhenTrue = jumpsWhenTrue ? jump : fall;
        ends[source].WhenFalse = jumpsWhenTrue ? fall : jump;
    }

    // The value of a branch condition known without running it: a discard pattern always matches, a constant is itself
    public static bool? GetConstantCondition(BasicBlock block) =>
        block.BranchValue switch
        {
            IIsPatternOperation { Pattern: IDiscardPatternOperation } => true,
            { ConstantValue: { HasValue: true, Value: bool value } } => value,
            _ => null
        };

    // An edge between two blocks; in a Gpu.SyncGrid body, through a split node when the edge crosses a gap. Returns the
    // node the edge leads to.
    private static int ConnectEdge(SplicedFlowContext context, ImmutableArray<BasicBlock> blocks,
        SplicedFlowNode[] starts, SplicedFlowNode[] ends, int source, int target)
    {
        if (context.Gaps is { } gaps &&
            RangeBefore(blocks, ends, source, blocks.Length) is { } before &&
            RangeAfter(blocks, starts, target, blocks.Length) is { } after &&
            gaps.HasGap(before.Last, after.First))
        {
            var split = context.NewNode();
            context.Record(
                static (recorder, node) =>
                    recorder.OnSplit(new SplitPoint(SplitKind.Gap, null, null, null, null), node),
                split.Index
            );
            SplicedFlowContext.Connect(ends[source], split);
            SplicedFlowContext.Connect(split, starts[target]);
            return split.Index;
        }

        SplicedFlowContext.Connect(ends[source], starts[target]);
        return starts[target].Index;
    }

    // The element range of the last operation run before leaving block, looking back through empty blocks
    private static SyncGridElementRange? RangeBefore(ImmutableArray<BasicBlock> blocks, SplicedFlowNode[] ends,
        int block, int depth)
    {
        if (ends[block].Last is { } last)
        {
            return last;
        }

        var predecessors = blocks[block].Predecessors;
        return depth > 0 && predecessors.Length == 1
            ? RangeBefore(blocks, ends, predecessors[0].Source.Ordinal, depth - 1)
            : null;
    }

    // The element range of the first operation run after entering block, looking ahead through empty blocks
    private static SyncGridElementRange? RangeAfter(ImmutableArray<BasicBlock> blocks, SplicedFlowNode[] starts,
        int block, int depth)
    {
        if (starts[block].First is { } first)
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
