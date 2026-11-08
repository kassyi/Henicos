using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Builds the <see cref="SplicedFlowGraph"/> of a body once, for every analysis that needs it, and lets each record
/// what the operations do (<see cref="IFlowRecorder"/>): the liveness of the cross-barrier rules
/// (<see cref="CrossBarrierLiveness"/>) and the uniformity analysis (<see cref="UniformityAnalysis"/>).
/// </summary>
/// <remarks>
/// <para>
/// The basic blocks of the Roslyn graph become nodes, connected as the graph connects them; a branch with no
/// destination (a <c>throw</c>) leads to the exit block (<see cref="SplicedFlowEdges"/>). The operations are walked in
/// the order C# evaluates them (<see cref="SplicedOperationWalker"/>): an assignment evaluates its value, then its
/// target (<see cref="SplicedAssignmentWalker"/>); a call its receiver and arguments, then splits, then writes its
/// <c>ref</c> and <c>out</c> arguments (<see cref="SplicedCallWalker"/>).
/// </para>
/// <para>
/// Bodies are spliced where they run, by one rule for every analysis (<see cref="SplicedBodySplicer"/>): the body of
/// <c>Gpu.ForUnroll</c> (as a loop) and of <c>Gpu.SyncGrid(body)</c> where the call expands it; a lambda where it is
/// created; a local function where it is called (once on each path: a recursive call adds nothing) and where it is
/// converted to a delegate. A lambda or local function body is opaque (<see cref="SplicedBody.IsOpaque"/>): it may run
/// again later, and a recorder decides what that means for it.
/// </para>
/// <para>
/// The state machine splits (<see cref="SplitPoint"/>) at a call that <see cref="SyncIntrinsicClassifier"/> marks
/// (barriers, warp shuffles) or that suspends (<see cref="SuspensionAnalysis"/>), at a <c>Gpu.SyncGrid(body)</c> with a
/// gap, and in each gap of its body (<see cref="SyncGridBody.IsSyncPoint"/>): between two operations of a block, or on
/// an edge between blocks, through a node of its own.
/// </para>
/// </remarks>
internal static class SplicedFlowBuilder
{
    /// <summary>
    /// Builds the graph of <paramref name="graph"/>, the body of a method, for <paramref name="recorders"/>.
    /// </summary>
    public static SplicedFlowGraph Build(ControlFlowGraph graph, SemanticModel model,
        ImmutableArray<IFlowRecorder> recorders)
    {
        var context = new SplicedFlowContext(model, recorders);
        var start = context.Current;
        var (entry, exit) = BuildGraph(context, graph, gaps: null, isRoot: true);
        SplicedFlowContext.Connect(start, entry);
        return new SplicedFlowGraph(context.ToNodes(), exit.Index);
    }

    /// <summary>
    /// Builds the nodes of <paramref name="graph"/>; returns the node of its entry block and the last node of its exit
    /// block.
    /// </summary>
    public static (SplicedFlowNode Entry, SplicedFlowNode Exit) BuildGraph(SplicedFlowContext context,
        ControlFlowGraph graph, SyncGridGaps? gaps, bool isRoot)
    {
        var (savedCurrent, savedGaps, savedLast, savedTop) =
            (context.Current, context.Gaps, context.LastRange, context.TopRange);
        context.Gaps = gaps;

        var blocks = graph.Blocks;
        var starts = new SplicedFlowNode[blocks.Length];
        var ends = new SplicedFlowNode[blocks.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            starts[i] = context.Current = context.NewNode();
            context.LastRange = null;
            BuildBlock(context, blocks[i], graph, isRoot);
            ends[i] = context.Current;
        }

        for (var i = 0; i < blocks.Length - 1; i++)
        {
            SplicedFlowEdges.ConnectBlock(context, blocks, starts, ends, i);
        }

        var result = (starts[0], ends[blocks.Length - 1]);
        (context.Current, context.Gaps, context.LastRange, context.TopRange) =
            (savedCurrent, savedGaps, savedLast, savedTop);
        return result;
    }

    // The operations of a block, then its branch value: the condition of a branch, or the value the method or a spliced
    // body returns
    private static void BuildBlock(SplicedFlowContext context, BasicBlock block, ControlFlowGraph graph, bool isRoot)
    {
        foreach (var operation in block.Operations)
        {
            SplicedOperationWalker.WalkTop(context, operation, graph);
        }

        if (block.BranchValue is not { } value)
        {
            return;
        }

        SplicedOperationWalker.WalkTop(context, value, graph);
        if (block.ConditionalSuccessor != null && SplicedFlowEdges.GetConstantCondition(block) == null)
        {
            context.Current.Condition = value;
            context.Current.Graph = graph;
        }
        else if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return && isRoot)
        {
            context.Current.ReturnValue = value;
            context.Current.Graph = graph;
        }
        else if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return)
        {
            foreach (var recorder in context.Recorders)
            {
                recorder.OnBodyReturn(value, graph, context.Current.Index);
            }
        }
    }
}
