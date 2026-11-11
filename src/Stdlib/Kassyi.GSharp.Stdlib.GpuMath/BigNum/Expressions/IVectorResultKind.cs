namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The steps of writing a vector node's result that differ between integer and floating-point results (#864).
/// </summary>
/// <typeparam name="TResult">The result a node hands up (all components of its elements).</typeparam>
/// <typeparam name="TTarget">The caller's target (an integer target holds only the values).</typeparam>
internal interface IVectorResultKind<TResult, TTarget>
    where TResult : struct, allows ref struct
    where TTarget : struct, allows ref struct
{
    /// <summary>Returns whether any component of a target overlaps any component of an input result.</summary>
    /// <param name="target">The checked target, or the default view (no target).</param>
    /// <param name="input">An input result of the node, or the default view (no such input).</param>
    /// <returns><see langword="true"/> when the two share a byte of device memory.</returns>
    static abstract bool Overlaps(TTarget target, TResult input);

    /// <summary>Returns whether any component of a target overlaps the words of a scalar operand.</summary>
    /// <param name="target">The checked target, or the default view (no target).</param>
    /// <param name="scalar">The scalar's words, or the default handle (no scalar).</param>
    /// <returns><see langword="true"/> when the two share a byte of device memory.</returns>
    static abstract bool Overlaps(TTarget target, GpuBigIntMemoryHandle scalar);

    /// <summary>Returns the result a node writes.</summary>
    /// <param name="cache">The node's result buffers.</param>
    /// <param name="workspace">The workspace the cache allocates in.</param>
    /// <param name="count">The number of elements.</param>
    /// <param name="wordsPerElement">The node's element width.</param>
    /// <param name="target">The checked target, or the default view to compute in the cache.</param>
    /// <returns>The storage of <paramref name="target"/> where it has some, the node's cache for the rest.</returns>
    static abstract TResult Destination(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        TTarget target);

    /// <summary>Copies the elements a node computed in its cache into a target.</summary>
    /// <param name="workspace">The workspace whose stream orders the copy.</param>
    /// <param name="computed">The result in the node's cache.</param>
    /// <param name="target">The checked target.</param>
    /// <param name="written">The elements the node wrote; the target keeps its other elements.</param>
    /// <returns>What the node hands up: the result in the target.</returns>
    static abstract TResult Deliver(GpuWorkspace workspace, TResult computed, TTarget target, DeviceElementCount written);
}
