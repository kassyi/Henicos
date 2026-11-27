using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A barrier, or a call that reaches one: the threads that must reach it, and the call itself.</summary>
/// <param name="Scope">The threads that must reach it.</param>
/// <param name="Call">The call of a device function or struct member that reaches a block barrier; none for a barrier.</param>
/// <param name="Graph">The Roslyn graph of the call.</param>
/// <param name="Node">The spliced flow node index where <paramref name="Call"/> was recorded, or -1.</param>
/// <param name="SplitIndex">The index of <paramref name="Call"/> within <see cref="UniformityNode.Splits"/> of <paramref name="Node"/>, or -1.</param>
internal readonly record struct BarrierSite(
    SyncScope Scope,
    BarrierCallSite? Call,
    ControlFlowGraph Graph,
    int Node = -1,
    int SplitIndex = -1);
