namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// An expression node that knows a static upper bound of its result in bits (<see cref="BigIntWidths"/>); its word
/// count is <c>BigIntWidths.Words(Bits)</c>.
/// </summary>
internal interface IBitBoundedExpression
{
    /// <summary>Gets the upper bound of the result in bits, at least 1.</summary>
    long Bits { get; }

    /// <summary>
    /// The bound of a signed node: its own when it has one, otherwise every bit of its words (a node implemented
    /// outside this assembly, or a vector reduction).
    /// </summary>
    static long Of(IGpuBigIntExpression node) =>
        node is IBitBoundedExpression bounded ? bounded.Bits : BigIntWidths.OfWords(Math.Max(1, node.NumWords));

    /// <summary>The bound of an unsigned node, as <see cref="Of(IGpuBigIntExpression)"/>.</summary>
    static long Of(IGpuBigUIntExpression node) =>
        node is IBitBoundedExpression bounded ? bounded.Bits : BigIntWidths.OfWords(Math.Max(1, node.NumWords));
}
