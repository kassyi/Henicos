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

/// <summary>An integer result: the target holds the values, the signs stay in the node's cache.</summary>
internal readonly struct IntVectorResultKind
    : IVectorResultKind<GpuBigIntArrayView, GpuBigUIntArrayView>, IVectorCompactKind<GpuBigIntArrayView>
{
    public static bool Overlaps(GpuBigUIntArrayView target, GpuBigIntArrayView input) =>
        DeviceRanges.Overlaps(target, input);

    public static bool Overlaps(GpuBigUIntArrayView target, GpuBigIntMemoryHandle scalar) =>
        DeviceRanges.Overlaps(target.Handle, scalar);

    public static GpuBigIntArrayView Destination(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigUIntArrayView target) =>
        cache.Signed(workspace, count, wordsPerElement, target);

    public static GpuBigIntArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigIntArrayView computed,
        GpuBigUIntArrayView target,
        DeviceElementCount written) =>
        VectorResultCache.Deliver(workspace, computed, target, written);

    public static int ComponentCount => 2;

    public static GpuBigIntArrayView Selected(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement) =>
        Destination(cache, workspace, count, wordsPerElement, default);

    public static int Length(GpuBigIntArrayView view) => view.Length;

    public static GpuBigIntArrayView Slice(GpuBigIntArrayView view, int elementOffset, int elementCount) =>
        view.Slice(elementOffset, elementCount);

    public static (IntPtr Pointer, int WordsPerElement) Component(GpuBigIntArrayView view, int index) =>
        index == 0 ? (view.Values.DevicePointer, view.WordsPerElement) : (view.Signs.DevicePointer, 1);

    public static void LaunchScatter(
        GpuWorkspace workspace,
        GpuBigIntArrayView output,
        GpuBigIntArrayView input,
        IntPtr predicates,
        IntPtr scan,
        DeviceElementCount count,
        uint gridDimX,
        uint blockDimX) =>
        GpuStreamCompactionKernels.LaunchCompactSignedVectorPtr(
            output.Values.DevicePointer,
            output.Signs.DevicePointer,
            input.Values.DevicePointer,
            input.Signs.DevicePointer,
            predicates,
            scan,
            input.Length,
            input.WordsPerElement,
            count.ToScanContext(),
            gridDimX: gridDimX,
            blockDimX: blockDimX,
            hStream: workspace.Stream
        );
}

/// <summary>A floating-point result: the target holds all three components.</summary>
internal readonly struct FloatVectorResultKind
    : IVectorResultKind<GpuBigFloatArrayView, GpuBigFloatArrayView>, IVectorCompactKind<GpuBigFloatArrayView>
{
    public static bool Overlaps(GpuBigFloatArrayView target, GpuBigFloatArrayView input) =>
        DeviceRanges.Overlaps(target, input);

    public static bool Overlaps(GpuBigFloatArrayView target, GpuBigIntMemoryHandle scalar) =>
        DeviceRanges.Overlaps(target, scalar);

    public static GpuBigFloatArrayView Destination(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigFloatArrayView target) =>
        VectorResultCache.HasTarget(target) ? target : cache.Float(workspace, count, wordsPerElement);

    public static GpuBigFloatArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigFloatArrayView computed,
        GpuBigFloatArrayView target,
        DeviceElementCount written) =>
        VectorResultCache.Deliver(workspace, computed, target, written);

    public static int ComponentCount => 3;

    public static GpuBigFloatArrayView Selected(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement) =>
        Destination(cache, workspace, count, wordsPerElement, default);

    public static int Length(GpuBigFloatArrayView view) => view.Length;

    public static GpuBigFloatArrayView Slice(GpuBigFloatArrayView view, int elementOffset, int elementCount) =>
        view.Slice(elementOffset, elementCount);

    public static (IntPtr Pointer, int WordsPerElement) Component(GpuBigFloatArrayView view, int index) =>
        index switch
        {
            0 => (view.Significands.DevicePointer, view.WordsPerElement),
            1 => (view.Exponents.DevicePointer, 1),
            _ => (view.Signs.DevicePointer, 1)
        };

    public static void LaunchScatter(
        GpuWorkspace workspace,
        GpuBigFloatArrayView output,
        GpuBigFloatArrayView input,
        IntPtr predicates,
        IntPtr scan,
        DeviceElementCount count,
        uint gridDimX,
        uint blockDimX) =>
        GpuStreamCompactionKernels.LaunchCompactFloatVectorPtr(
            output.Significands.DevicePointer,
            output.Exponents.DevicePointer,
            output.Signs.DevicePointer,
            input.Significands.DevicePointer,
            input.Exponents.DevicePointer,
            input.Signs.DevicePointer,
            predicates,
            scan,
            input.Length,
            input.WordsPerElement,
            count.ToScanContext(),
            gridDimX: gridDimX,
            blockDimX: blockDimX,
            hStream: workspace.Stream
        );
}

/// <summary>
/// Where a vector node writes its result: the caller's target, or its cache when the target shares words with an input
/// (#864). <see cref="Open"/> picks the destination after the children are evaluated, the node launches into
/// <see cref="Destination"/>, and <c>Close</c> returns the result.
/// </summary>
/// <remarks>
/// A target that overlaps an input is not written by the node's kernel or host loop: an element would be written before
/// another element read its input. The node computes into its cache and <c>Close</c> copies the result into the
/// target, as the scalar roots do (#636). A disjoint target is written directly, with no cache and no copy.
/// </remarks>
internal readonly ref struct VectorOutput<TKind, TResult, TTarget>
    where TKind : IVectorResultKind<TResult, TTarget>
    where TResult : struct, allows ref struct
    where TTarget : struct, allows ref struct
{
    private readonly TTarget _target;
    private readonly int _count;
    private readonly bool _staged;

    private VectorOutput(TResult destination, TTarget target, int count, bool staged)
    {
        Destination = destination;
        _target = target;
        _count = count;
        _staged = staged;
    }

    /// <summary>Gets the result the node's kernel or host loop writes.</summary>
    public TResult Destination { get; }

    /// <summary>
    /// Picks the destination of a node with the inputs <paramref name="input"/>, <paramref name="other"/> (the right
    /// operand of a Zip) and <paramref name="scalar"/> (the words of a scalar operand); an absent input is the default.
    /// </summary>
    public static VectorOutput<TKind, TResult, TTarget> Open(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        TTarget checkedTarget,
        TResult input,
        TResult other,
        GpuBigIntMemoryHandle scalar)
    {
        var staged = TKind.Overlaps(checkedTarget, input) ||
            TKind.Overlaps(checkedTarget, other) ||
            TKind.Overlaps(checkedTarget, scalar);
        var destination = TKind.Destination(cache, workspace, count, wordsPerElement, staged ? default : checkedTarget);
        return new VectorOutput<TKind, TResult, TTarget>(destination, checkedTarget, count, staged);
    }

    /// <summary>
    /// Returns the result of a node that wrote every element of the destination (a kernel): the target after a staged
    /// run (copied from the cache), the destination otherwise.
    /// </summary>
    public TResult Close(GpuWorkspace workspace) => Close(workspace, DeviceElementCount.Static(_count));

    /// <summary>
    /// Returns the result of a node that wrote only the first <paramref name="written"/> elements (a host loop over a
    /// device count): a staged run copies only those, on the device, so that the target keeps its other elements as a
    /// target written directly does.
    /// </summary>
    public TResult Close(GpuWorkspace workspace, DeviceElementCount written) =>
        _staged ? TKind.Deliver(workspace, Destination, _target, written) : Destination;
}

/// <summary>
/// Opens a <see cref="VectorOutput{TKind, TResult, TTarget}"/> of an integer or a floating-point node.
/// </summary>
internal static class VectorOutput
{
    /// <summary>
    /// Opens the output of an integer node; see <see cref="VectorOutput{TKind, TResult, TTarget}.Open"/>.
    /// </summary>
    public static VectorOutput<IntVectorResultKind, GpuBigIntArrayView, GpuBigUIntArrayView> Open(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigUIntArrayView checkedTarget,
        GpuBigIntArrayView input,
        GpuBigIntArrayView other = default,
        GpuBigIntMemoryHandle scalar = default) =>
        VectorOutput<IntVectorResultKind, GpuBigIntArrayView, GpuBigUIntArrayView>.Open(
            cache,
            workspace,
            count,
            wordsPerElement,
            checkedTarget,
            input,
            other,
            scalar
        );

    /// <summary>
    /// Opens the output of a floating-point node; see <see cref="VectorOutput{TKind, TResult, TTarget}.Open"/>.
    /// </summary>
    public static VectorOutput<FloatVectorResultKind, GpuBigFloatArrayView, GpuBigFloatArrayView> Open(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigFloatArrayView checkedTarget,
        GpuBigFloatArrayView input,
        GpuBigFloatArrayView other = default,
        GpuBigIntMemoryHandle scalar = default) =>
        VectorOutput<FloatVectorResultKind, GpuBigFloatArrayView, GpuBigFloatArrayView>.Open(
            cache,
            workspace,
            count,
            wordsPerElement,
            checkedTarget,
            input,
            other,
            scalar
        );
}
