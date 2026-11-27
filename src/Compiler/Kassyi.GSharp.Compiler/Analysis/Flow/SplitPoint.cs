using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A point where the state machine of the CPU dialects splits.</summary>
/// <param name="Kind">What splits.</param>
/// <param name="Syntax">The call (none for a <see cref="SplitKind.Gap"/>).</param>
/// <param name="BarrierScope">
/// The threads that must reach it when it is a barrier, or a call that reaches a block barrier (GSH0037); none otherwise.
/// </param>
/// <param name="BarrierCall">The call of a device function or struct member that reaches a block barrier.</param>
/// <param name="Graph">The Roslyn graph of the call.</param>
/// <param name="Operation">The operation that splits (when <paramref name="Kind"/> is <see cref="SplitKind.Call"/>).</param>
/// <param name="Target">The called method symbol for member splits, when known.</param>
internal readonly record struct SplitPoint(
    SplitKind Kind,
    SyntaxNode? Syntax,
    SyncScope? BarrierScope,
    BarrierCallSite? BarrierCall,
    Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? Graph,
    Microsoft.CodeAnalysis.IOperation? Operation = null,
    IMethodSymbol? Target = null);
