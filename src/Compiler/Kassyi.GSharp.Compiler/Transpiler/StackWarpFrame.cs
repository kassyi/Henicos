namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The frame of the method whose native stack form is being written, when it reaches a warp collective: where its
/// collectives and the frames of its calls lie in the visit counts of the kernel (<see cref="WarpFrameLayout"/>).
/// </summary>
/// <param name="Layout">The layout of the method's frame.</param>
/// <param name="TreeOrdinal">
/// The ordinal of the method's syntax tree, to read the original span of a lowered node.
/// </param>
internal sealed record StackWarpFrame(WarpFrameLayout Layout, int TreeOrdinal);
