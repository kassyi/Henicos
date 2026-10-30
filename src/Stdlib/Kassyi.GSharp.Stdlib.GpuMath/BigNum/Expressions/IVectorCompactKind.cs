namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The steps of a <c>Where</c> that differ between integer and floating-point vectors: the selected elements in the
/// node's cache, the components the compaction clears, and the scatter launch. <see cref="WhereDeviceState"/> and
/// <c>StreamCompactionOrchestrator.Compact</c> are written once over it.
/// </summary>
/// <typeparam name="TResult">The vector view (all components of its elements).</typeparam>
internal interface IVectorCompactKind<TResult>
    where TResult : struct, allows ref struct
{
    /// <summary>Gets the number of device arrays of a vector (int: values and signs; float: three).</summary>
    static abstract int ComponentCount { get; }

    /// <summary>Lends the selected elements of a <c>Where</c> from the node's cache.</summary>
    /// <param name="cache">The node's result buffers.</param>
    /// <param name="workspace">The workspace the cache allocates in.</param>
    /// <param name="count">The capacity.</param>
    /// <param name="wordsPerElement">The element width.</param>
    /// <returns>A view of <paramref name="count"/> elements of the cache.</returns>
    static abstract TResult Selected(VectorResultCache cache, GpuWorkspace workspace, int count, int wordsPerElement);

    /// <summary>Gets the number of elements of a view.</summary>
    /// <param name="view">The view.</param>
    /// <returns>Its length.</returns>
    static abstract int Length(TResult view);

    /// <summary>Gets a window of a view.</summary>
    /// <param name="view">The view.</param>
    /// <param name="elementOffset">The first element.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>The window.</returns>
    static abstract TResult Slice(TResult view, int elementOffset, int elementCount);

    /// <summary>Gets one device array of a view.</summary>
    /// <param name="view">The view.</param>
    /// <param name="index">The component, below <see cref="ComponentCount"/>.</param>
    /// <returns>Its device pointer and its words per element.</returns>
    static abstract (IntPtr Pointer, int WordsPerElement) Component(TResult view, int index);

    /// <summary>Launches the scatter of the selected elements, all components in one launch.</summary>
    /// <param name="workspace">The workspace whose stream runs the launch.</param>
    /// <param name="output">The compacted elements.</param>
    /// <param name="input">The elements to select from.</param>
    /// <param name="predicates">The predicate flags.</param>
    /// <param name="scan">The exclusive scan of the flags: each selected element's position.</param>
    /// <param name="count">The number of input elements; the ones after it are not read.</param>
    /// <param name="gridDimX">One thread per input element.</param>
    /// <param name="blockDimX">The threads per block.</param>
    static abstract void LaunchScatter(
        GpuWorkspace workspace,
        TResult output,
        TResult input,
        IntPtr predicates,
        IntPtr scan,
        DeviceElementCount count,
        uint gridDimX,
        uint blockDimX);
}
