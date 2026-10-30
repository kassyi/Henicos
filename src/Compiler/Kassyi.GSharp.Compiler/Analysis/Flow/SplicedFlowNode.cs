using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// A node of a <see cref="SplicedFlowGraph"/>: a straight run of operations of one basic block (or part of one, where a
/// body is spliced in), with the nodes that may follow it. What the operations do is recorded by each
/// <see cref="IFlowRecorder"/> under the index of the node.
/// </summary>
internal sealed class SplicedFlowNode(int index)
{
    public int Index { get; } = index;

    /// <summary>The nodes that may run next, each once.</summary>
    public ImmutableArray<int>.Builder Successors { get; } = ImmutableArray.CreateBuilder<int>();

    /// <summary>The condition evaluated at the end of the node when it branches.</summary>
    public IOperation? Condition { get; set; }

    /// <summary>The value the node returns from the analyzed method (not from a spliced body).</summary>
    public IOperation? ReturnValue { get; set; }

    /// <summary>The Roslyn graph that <see cref="Condition"/> and <see cref="ReturnValue"/> belong to.</summary>
    public ControlFlowGraph? Graph { get; set; }

    /// <summary>The successor taken when <see cref="Condition"/> is true (-1 when the node does not branch).</summary>
    public int WhenTrue { get; set; } = -1;

    /// <summary>The successor taken when <see cref="Condition"/> is false (-1 when the node does not branch).</summary>
    public int WhenFalse { get; set; } = -1;

    /// <summary>The element range of the first top-level operation of the node in a <c>Gpu.SyncGrid</c> body.</summary>
    public SyncGridElementRange? First { get; set; }

    /// <summary>The element range of the last top-level operation of the node in a <c>Gpu.SyncGrid</c> body.</summary>
    public SyncGridElementRange? Last { get; set; }
}
