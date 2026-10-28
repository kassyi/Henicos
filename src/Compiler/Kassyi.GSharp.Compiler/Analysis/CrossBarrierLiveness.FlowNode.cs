namespace Kassyi.GSharp.Compiler.Analysis;

internal sealed partial class CrossBarrierLiveness
{
    // A node of the graph: a straight run of events, with the nodes that may follow it
    private sealed class FlowNode(int index)
    {
        public int Index { get; } = index;

        public ImmutableArray<FlowEvent>.Builder Events { get; } = ImmutableArray.CreateBuilder<FlowEvent>();

        public ImmutableArray<int>.Builder Successors { get; } = ImmutableArray.CreateBuilder<int>();

        // The element ranges of the first and the last top-level operation of the node in a Gpu.SyncGrid body
        public ElementRange? First { get; set; }

        public ElementRange? Last { get; set; }
    }
}
