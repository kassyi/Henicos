namespace Kassyi.GSharp.Compiler.Transpiler.Frames;

/// <summary>The frame of a CPU state machine, decided before the native or managed writer spells it.</summary>
/// <param name="CopiedParameters">The by-value parameters copied into the frame on the first entry, in order.</param>
/// <param name="Slots">The hoisted locals, in the order the lowering declares them.</param>
/// <param name="CallFrameCallees">The function of each call that may suspend, in frame index order.</param>
/// <param name="WarpSiteCount">The warp collectives of the body, numbered from 0.</param>
internal sealed record StateMachineFrame(
    ImmutableArray<IParameterSymbol> CopiedParameters,
    ImmutableArray<FrameSlot> Slots,
    ImmutableArray<IMethodSymbol> CallFrameCallees,
    int WarpSiteCount);
