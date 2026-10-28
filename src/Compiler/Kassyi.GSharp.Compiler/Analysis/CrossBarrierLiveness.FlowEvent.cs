namespace Kassyi.GSharp.Compiler.Analysis;

internal sealed partial class CrossBarrierLiveness
{
    // An event of a node of the graph: a local read, written or pinned, or a split
    private enum FlowEventKind
    {
        Read,
        Write,
        Split,

        // A delegate that may run after any later split captures the local (forward, AddPinnedAcrossSplit)
        Pin
    }

    private readonly struct FlowEvent(FlowEventKind kind, int local)
    {
        public static readonly FlowEvent Split = new(FlowEventKind.Split, -1);

        public FlowEventKind Kind { get; } = kind;

        public int Local { get; } = local;

        public static FlowEvent Read(int local) => new(FlowEventKind.Read, local);

        public static FlowEvent Write(int local) => new(FlowEventKind.Write, local);

        public static FlowEvent Pin(int local) => new(FlowEventKind.Pin, local);
    }
}
