namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Whether the result of a node is still in the node's cache: a node evaluated again lends the same buffers and
/// overwrites its earlier result. The default lease (a leaf, an empty result, a node outside GpuMath) never
/// ends.
/// </summary>
/// <param name="cache">The cache that lent the result, or <see langword="null"/>.</param>
/// <param name="generation">The generation of <paramref name="cache"/> when the result was handed up.</param>
internal readonly struct VectorLease(VectorResultCache? cache, int generation)
{
    /// <summary>Gets whether the cache has not lent its buffers again since the result was handed up.</summary>
    public bool IsCurrent => cache is null || cache.IsCurrent(generation);
}
