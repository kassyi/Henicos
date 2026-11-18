namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>The state-machine form of a method body that splits across barriers, warp operations, or calls.</summary>
/// <param name="states">The numbered states, in order from 0.</param>
/// <param name="isDeviceFunction">Whether the method is a device function rather than a kernel.</param>
/// <param name="warpSiteCount">The number of warp collective sites allocated.</param>
/// <param name="callFrames">The called suspending device functions in call-frame index order.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrStateMachine(ImmutableArray<IrState> states, bool isDeviceFunction, int warpSiteCount,
    ImmutableArray<IMethodSymbol> callFrames, IrSource source) : IrNode(source)
{
    /// <summary>The numbered states, in order from 0.</summary>
    public ImmutableArray<IrState> States { get; } = states;

    /// <summary>Whether the method is a device function rather than a kernel.</summary>
    public bool IsDeviceFunction { get; } = isDeviceFunction;

    /// <summary>The number of warp collective sites allocated.</summary>
    public int WarpSiteCount { get; } = warpSiteCount;

    /// <summary>The called suspending device functions in call-frame index order.</summary>
    public ImmutableArray<IMethodSymbol> CallFrames { get; } = callFrames;

    /// <summary>This state machine with <paramref name="states"/>; this node itself when unchanged.</summary>
    public IrStateMachine Update(ImmutableArray<IrState> states) =>
        states == States
            ? this
            : new IrStateMachine(states, IsDeviceFunction, WarpSiteCount, CallFrames, Source);
}
