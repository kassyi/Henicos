namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A node of the graph: a straight run of events, with the nodes that may follow it (<see cref="CrossBarrierLiveness"/>).</summary>
internal sealed class LivenessNode(int index)
{
    public int Index { get; } = index;

    public ImmutableArray<LivenessEvent>.Builder Events { get; } = ImmutableArray.CreateBuilder<LivenessEvent>();

    public ImmutableArray<int>.Builder Successors { get; } = ImmutableArray.CreateBuilder<int>();

    // The element ranges of the first and the last top-level operation of the node in a Gpu.SyncGrid body
    public SyncGridElementRange? First { get; set; }

    public SyncGridElementRange? Last { get; set; }
}
