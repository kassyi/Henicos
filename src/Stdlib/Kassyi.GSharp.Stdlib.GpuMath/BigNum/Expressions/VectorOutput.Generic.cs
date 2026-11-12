namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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
