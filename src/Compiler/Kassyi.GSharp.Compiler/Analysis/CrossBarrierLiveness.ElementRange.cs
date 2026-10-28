namespace Kassyi.GSharp.Compiler.Analysis;

internal sealed partial class CrossBarrierLiveness
{
    // The statements of a Gpu.SyncGrid body that an operation belongs to, by index in SyncGridBody.Flatten
    private readonly record struct ElementRange(int First, int Last);
}
