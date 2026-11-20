namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What the native stack form adds to a call (<see cref="StackWarpEmitter.Plan"/>).</summary>
internal enum StackWarpCallKind
{
    /// <summary>Nothing: the call is written as it is.</summary>
    None,

    /// <summary>
    /// A warp collective: the call becomes <see cref="StackWarpCall.Function"/>(frame, site, "place", arguments).
    /// </summary>
    Collective,

    /// <summary>
    /// A call of a device function that reaches a warp collective: it passes the offset of the callee's frame last.
    /// </summary>
    FrameCall
}
