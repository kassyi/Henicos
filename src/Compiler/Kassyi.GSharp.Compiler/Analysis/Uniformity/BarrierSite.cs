using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A barrier, or a call that reaches one: the threads that must reach it, and the call itself.</summary>
/// <param name="Scope">The threads that must reach it.</param>
/// <param name="Call">The call of a device function that reaches a block barrier; none for a barrier.</param>
/// <param name="Graph">The Roslyn graph of the call.</param>
internal readonly record struct BarrierSite(SyncScope Scope, IInvocationOperation? Call, ControlFlowGraph Graph);
