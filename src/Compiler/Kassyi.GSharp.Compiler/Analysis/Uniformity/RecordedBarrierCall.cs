namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>An additional barrier-reaching call sharing a syntax node with a primary <see cref="BarrierSite"/>.</summary>
/// <param name="Call">The barrier call site.</param>
/// <param name="Node">The spliced flow node index where <paramref name="Call"/> was recorded.</param>
/// <param name="SplitIndex">The index of <paramref name="Call"/> within <see cref="UniformityNode.Splits"/> of <paramref name="Node"/>.</param>
internal readonly record struct RecordedBarrierCall(BarrierCallSite Call, int Node, int SplitIndex);
