namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>An event of a node of the graph of <see cref="CrossBarrierLiveness"/>.</summary>
internal readonly struct LivenessEvent(LivenessEventKind kind, int local)
{
    public static readonly LivenessEvent Split = new(LivenessEventKind.Split, -1);

    public LivenessEventKind Kind { get; } = kind;

    public int Local { get; } = local;

    public static LivenessEvent Read(int local) => new(LivenessEventKind.Read, local);

    public static LivenessEvent Write(int local) => new(LivenessEventKind.Write, local);

    public static LivenessEvent Pin(int local) => new(LivenessEventKind.Pin, local);

    public static LivenessEvent Change(int local) => new(LivenessEventKind.Change, local);
}
