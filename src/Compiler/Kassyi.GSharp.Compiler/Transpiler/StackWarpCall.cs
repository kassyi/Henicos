namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>The plan of a call in the native stack form of a method that reaches a warp collective.</summary>
/// <param name="Kind">What the call gets.</param>
/// <param name="Function">The prelude function of a collective (<see cref="WarpShuffleShape.StackFunction"/>).</param>
/// <param name="Site">The site of a collective in the frame of the calling method.</param>
/// <param name="Place">The place of a collective in the C# source, for the error messages.</param>
/// <param name="Offset">The offset of the callee's frame from the caller's frame.</param>
internal readonly record struct StackWarpCall(
    StackWarpCallKind Kind,
    string Function,
    int Site,
    string Place,
    int Offset)
{
    /// <summary>A call that the stack form writes as it is.</summary>
    public static StackWarpCall None => new(StackWarpCallKind.None, "", 0, "", 0);

    /// <summary>A warp collective at <paramref name="site"/>.</summary>
    public static StackWarpCall Collective(string function, int site, string place) =>
        new(StackWarpCallKind.Collective, function, site, place, 0);

    /// <summary>A call whose callee's frame starts <paramref name="offset"/> slots after the caller's.</summary>
    public static StackWarpCall FrameCall(int offset) => new(StackWarpCallKind.FrameCall, "", 0, "", offset);
}
