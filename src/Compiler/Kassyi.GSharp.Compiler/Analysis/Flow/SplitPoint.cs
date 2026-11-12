using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A point where the state machine of the CPU dialects splits.</summary>
/// <param name="Kind">What splits.</param>
/// <param name="Syntax">The call (none for a <see cref="SplitKind.Gap"/>).</param>
/// <param name="BarrierScope">
/// The threads that must reach it when it is a barrier, or a call that reaches a block barrier (GSH0037); none otherwise.
/// </param>
/// <param name="BarrierCall">The call of a device function that reaches a block barrier.</param>
/// <param name="Graph">The Roslyn graph of the call.</param>
internal readonly record struct SplitPoint(
    SplitKind Kind,
    SyntaxNode? Syntax,
    SyncScope? BarrierScope,
    IInvocationOperation? BarrierCall,
    Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? Graph);
