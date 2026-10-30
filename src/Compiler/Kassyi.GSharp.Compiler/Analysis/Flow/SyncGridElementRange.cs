namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>The statements of a Gpu.SyncGrid body that an operation belongs to, by index in SyncGridBody.Flatten (<see cref="SplicedFlowBuilder"/>).</summary>
internal readonly record struct SyncGridElementRange(int First, int Last);
