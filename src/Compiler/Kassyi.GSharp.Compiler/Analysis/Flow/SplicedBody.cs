using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Why a body is spliced into a <see cref="SplicedFlowGraph"/> where it is.</summary>
internal enum SplicedBodyKind
{
    /// <summary>The body of <c>Gpu.ForUnroll</c> (a loop) or <c>Gpu.SyncGrid(body)</c>, where the call expands it.</summary>
    Macro,

    /// <summary>A lambda, where it is created (also a lambda that a <c>Gpu</c> macro or StackLinq runs in place).</summary>
    Lambda,

    /// <summary>A local function, where it is called.</summary>
    LocalFunctionCall,

    /// <summary>A local function, where it is converted to a delegate.</summary>
    LocalFunctionDelegate
}

/// <summary>
/// A body that <see cref="SplicedFlowBuilder"/> splices into the graph. Every kind but
/// <see cref="SplicedBodyKind.Macro"/> is opaque: the state machine does not split it, and a lambda or a delegate may
/// run it at any later time.
/// </summary>
/// <param name="Kind">Why the body is where it is.</param>
/// <param name="Syntax">The lambda or the local function declaration.</param>
/// <param name="Parameters">The parameters of the lambda or local function.</param>
/// <param name="HasUniformIndex">Whether the last parameter is the index of a <c>Gpu.ForUnroll</c> body.</param>
internal readonly record struct SplicedBody(
    SplicedBodyKind Kind,
    SyntaxNode Syntax,
    ImmutableArray<IParameterSymbol> Parameters,
    bool HasUniformIndex = false)
{
    /// <summary>Whether the body may run at another time than where it is spliced, outside the state machine's splits.</summary>
    public bool IsOpaque => Kind != SplicedBodyKind.Macro;
}

/// <summary>What kind of point splits the state machine (<see cref="SplitPoint"/>).</summary>
internal enum SplitKind
{
    /// <summary>A barrier, a warp shuffle or collective, or a call of a device function that suspends.</summary>
    Call,

    /// <summary>A <c>Gpu.SyncGrid(body)</c> whose body has a gap: a grid barrier where it is called.</summary>
    SyncGridCall,

    /// <summary>A gap between two statements of a <c>Gpu.SyncGrid(body)</c>, where the expansion syncs the grid.</summary>
    Gap
}

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
