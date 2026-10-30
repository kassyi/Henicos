using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The result buffers a vector expression node keeps between evaluations (design 4.6).
/// </summary>
/// <remarks>
/// <para>
/// Each array grows when an evaluation needs more elements than it holds and is reused otherwise, like the buffer of
/// <c>ArrayBufferWriter&lt;T&gt;</c>; the node lends a view of exactly the evaluated length, valid until the next evaluation
/// or <see cref="Dispose"/>. The arrays have separate capacities: an evaluation into a caller's target uses only the
/// signs of the cache.
/// </para>
/// <para>
/// The cache owns only what it allocated; a caller's target is checked (<see cref="TargetValues"/>) and borrowed, never
/// stored.
/// </para>
/// <para>
/// A target that shares words with an input of the node (in place, or shifted by some elements) is not written by the
/// node's kernel or host loop: an element would be written before another element read its input. The node computes
/// into its cache and <see cref="Deliver(GpuWorkspace, GpuBigIntArrayView, GpuBigUIntArrayView, DeviceElementCount)"/> copies the result
/// into the target (#864), as the scalar roots do (#636).
/// </para>
/// </remarks>
internal sealed class VectorResultCache : IDisposable
{
    private GpuBigUIntArray _values;
    private GpuBigUIntArray _signs;
    private GpuBigUIntArray _exponents;
    private GpuBigUIntArray _scan;

    /// <summary>Lends <paramref name="count"/> elements of the cached values.</summary>
    public GpuBigUIntArrayView Values(GpuWorkspace workspace, int count, int wordsPerElement) =>
        Get(ref _values, workspace, count, wordsPerElement);

    /// <summary>Lends <paramref name="count"/> sign words.</summary>
    public GpuBigUIntArrayView Signs(GpuWorkspace workspace, int count) => Get(ref _signs, workspace, count, 1);

    /// <summary>Lends <paramref name="count"/> exponent words.</summary>
    public GpuBigUIntArrayView Exponents(GpuWorkspace workspace, int count) => Get(ref _exponents, workspace, count, 1);

    /// <summary>Lends <paramref name="count"/> words of scratch (the prefix sum of a Where).</summary>
    public GpuBigUIntArrayView Scan(GpuWorkspace workspace, int count) => Get(ref _scan, workspace, count, 1);

    /// <summary>
    /// Lends a signed result of <paramref name="count"/> elements, with the values from <paramref name="checkedTarget"/>
    /// (the result of <see cref="TargetValues"/>) unless it is the default view.
    /// </summary>
    public GpuBigIntArrayView Signed(
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigUIntArrayView checkedTarget) =>
        new(HasTarget(checkedTarget) ? checkedTarget : Values(workspace, count, wordsPerElement), Signs(workspace, count));

    /// <summary>Lends a floating-point result of <paramref name="count"/> elements.</summary>
    public GpuBigFloatArrayView Float(GpuWorkspace workspace, int count, int wordsPerElement) =>
        new(Values(workspace, count, wordsPerElement), Exponents(workspace, count), Signs(workspace, count));

    /// <summary>
    /// Returns whether a caller passed a target: only the default view means none. A target of 0 elements is a target
    /// (its handle is the default one, since a zero-word allocation has no buffer) and goes to the checks.
    /// </summary>
    public static bool HasTarget(GpuBigUIntArrayView target) => target.Workspace is not null;

    /// <inheritdoc cref="HasTarget(GpuBigUIntArrayView)"/>
    public static bool HasTarget(GpuBigFloatArrayView target) => target.Workspace is not null;

    /// <summary>
    /// Checks a caller's target and borrows its first <paramref name="count"/> elements.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The element width differs from the expression's (the kernel would write at the wrong offsets), or the target is
    /// shorter than the evaluation.
    /// </exception>
    public static GpuBigUIntArrayView TargetValues(GpuBigUIntArrayView target, int count, int wordsPerElement)
    {
        ThrowIfWrongShape(target.WordsPerElement, target.Length, count, wordsPerElement);
        return target.Slice(0, count);
    }

    /// <summary>
    /// Checks a caller's floating-point target and borrows its first <paramref name="count"/> elements.
    /// </summary>
    /// <exception cref="ArgumentException">The element width differs, or the target is too short.</exception>
    public static GpuBigFloatArrayView TargetFloat(GpuBigFloatArrayView target, int count, int wordsPerElement)
    {
        ThrowIfWrongShape(target.WordsPerElement, target.Length, count, wordsPerElement);
        return target.Slice(0, count);
    }

    /// <summary>
    /// Copies the values of the first <paramref name="written"/> elements of a result that a node computed in its cache,
    /// because the target shares words with one of its inputs (#864), into the target; returns the result with the
    /// target's values and the cache's signs. The target's other elements keep their values.
    /// </summary>
    public static GpuBigIntArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigIntArrayView computed,
        GpuBigUIntArrayView checkedTarget,
        DeviceElementCount written)
    {
        CopyCounted(workspace, checkedTarget, computed.Values, written);
        return new GpuBigIntArrayView(checkedTarget, computed.Signs);
    }

    /// <summary>
    /// The floating-point form of
    /// <see cref="Deliver(GpuWorkspace, GpuBigIntArrayView, GpuBigUIntArrayView, DeviceElementCount)"/>: the three
    /// components of the first <paramref name="written"/> elements; returns the target.
    /// </summary>
    public static GpuBigFloatArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigFloatArrayView computed,
        GpuBigFloatArrayView checkedTarget,
        DeviceElementCount written)
    {
        CopyCounted(workspace, checkedTarget.Significands, computed.Significands, written);
        CopyCounted(workspace, checkedTarget.Exponents, computed.Exponents, written);
        CopyCounted(workspace, checkedTarget.Signs, computed.Signs, written);
        return checkedTarget;
    }

    /// <summary>
    /// Copies a floating-point result (a node's cache, or a leaf's input array) into the target, each component with
    /// <see cref="CopyValues"/>, and returns the target (#864).
    /// </summary>
    public static GpuBigFloatArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigFloatArrayView computed,
        GpuBigFloatArrayView checkedTarget)
    {
        CopyValues(workspace, checkedTarget.Significands, computed.Significands);
        CopyValues(workspace, checkedTarget.Exponents, computed.Exponents);
        CopyValues(workspace, checkedTarget.Signs, computed.Signs);
        return checkedTarget;
    }

    /// <summary>
    /// Copies the first <paramref name="count"/> elements of <paramref name="source"/> into
    /// <paramref name="destination"/> (the same width; a node's cache into a target, which never share words). A count
    /// on the device is read by the copy kernel, so the host neither waits for it nor reads it, and a graph capture
    /// records the copy.
    /// </summary>
    private static void CopyCounted(
        GpuWorkspace workspace,
        GpuBigUIntArrayView destination,
        GpuBigUIntArrayView source,
        DeviceElementCount count)
    {
        if (count.IsStatic)
        {
            var elements = Math.Min(count.Limit, destination.Length);
            CopyValues(workspace, destination.Slice(0, elements), source.Slice(0, elements));
            return;
        }

        var words = destination.TotalWords;
        if (words == 0)
        {
            return;
        }

        // A grid-stride loop over the capacity's words; the threads past the count's words copy nothing
        const int threadsPerBlock = 256;
        var gridDimX = (uint)Math.Min((words + threadsPerBlock - 1) / threadsPerBlock, 4096);
        var ctx = new GpuBigIntVectorKernels.AotContexts.CountedCopy
        {
            Destination = destination.DevicePointer,
            Source = source.DevicePointer,
            WordsPerElement = destination.WordsPerElement,
            Count = count.ToContext()
        };
        GpuBigIntVectorKernels.LaunchCopyCountedElementsKernelPtr(
            ctx, gridDimX, 1, 1, threadsPerBlock, 1, 1, 0, workspace.Stream);
    }

    /// <summary>
    /// Copies the words of <paramref name="source"/> into <paramref name="destination"/> (the same length): nothing for
    /// the same words, through a temporary when the ranges partly overlap (<c>cuMemcpyDtoD</c> does not promise
    /// overlapping copies), directly otherwise, as the scalar leaves do (#636).
    /// </summary>
    public static void CopyValues(GpuWorkspace workspace, GpuBigUIntArrayView destination, GpuBigUIntArrayView source)
    {
        var words = destination.TotalWords;
        if (words == 0 || destination.DevicePointer == source.DevicePointer)
        {
            return;
        }

        var provider = workspace.Provider;
        if (DeviceRanges.Overlaps(destination, source))
        {
            using var staging = new GpuBigUInt(workspace, words);
            provider.CopyDeviceToDevice(staging.DevicePointer, source.DevicePointer, words, workspace.Stream);
            provider.CopyDeviceToDevice(destination.DevicePointer, staging.DevicePointer, words, workspace.Stream);
            return;
        }

        provider.CopyDeviceToDevice(destination.DevicePointer, source.DevicePointer, words, workspace.Stream);
    }

    /// <summary>
    /// Returns an empty signed result that borrows nothing (an evaluation of no elements, design n8).
    /// </summary>
    public static GpuBigIntArrayView EmptySigned(GpuWorkspace workspace, int wordsPerElement) =>
        new(new GpuBigUIntArrayView(workspace, default, 0, wordsPerElement), new GpuBigUIntArrayView(workspace, default, 0, 1));

    /// <summary>
    /// Returns an empty floating-point result that borrows nothing.
    /// </summary>
    public static GpuBigFloatArrayView EmptyFloat(GpuWorkspace workspace, int wordsPerElement) =>
        new(new GpuBigUIntArrayView(workspace, default, 0, wordsPerElement),
            new GpuBigUIntArrayView(workspace, default, 0, 1),
            new GpuBigUIntArrayView(workspace, default, 0, 1));

    /// <summary>
    /// Returns every array the cache allocated; a second call does nothing.
    /// </summary>
    /// <exception cref="AggregateException">More than one release failed.</exception>
    public void Dispose()
    {
        // Every release runs even when an earlier one throws (a failed capture refuses the clear), and each field is
        // cleared either way, so that a second call frees nothing twice. A default array owns nothing, so its Dispose
        // does nothing. The errors are thrown once at the end, as in GpuBigIntExpr (RunCleanupStep and
        // ThrowIfCleanupFailed).
        List<Exception>? errors = null;
        Release(ref _values, ref errors);
        Release(ref _signs, ref errors);
        Release(ref _exponents, ref errors);
        Release(ref _scan, ref errors);
        ThrowIfReleaseFailed(errors);
    }

    /// <summary>
    /// Disposes <paramref name="field"/> and clears it; an exception is collected, so that the next releases still run.
    /// </summary>
    internal static void Release<T>(ref T field, ref List<Exception>? errors)
        where T : struct, IDisposable
    {
        try
        {
            field.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }
        finally
        {
            field = default;
        }
    }

    /// <summary>
    /// Nothing when every release succeeded; the exception itself (its type and stack kept) when one failed, and an
    /// <see cref="AggregateException"/> of all of them in order otherwise.
    /// </summary>
    internal static void ThrowIfReleaseFailed(List<Exception>? errors)
    {
        switch (errors)
        {
            case null:
                return;
            case [var single]:
                ExceptionDispatchInfo.Throw(single);
                break;
            default:
                throw new AggregateException("Releasing the cached result arrays failed.", errors);
        }
    }

    private static void ThrowIfWrongShape(int targetWordsPerElement, int targetLength, int count, int wordsPerElement)
    {
        if (targetWordsPerElement != wordsPerElement)
        {
            throw new ArgumentException(
                $"The target has {targetWordsPerElement} words per element; the expression produces {wordsPerElement}.",
                "targetResult");
        }

        if (targetLength < count)
        {
            throw new ArgumentException(
                $"The target has {targetLength} elements; the evaluation produces {count}.", "targetResult");
        }
    }

    // The unit of growth is one array: the new array is allocated before the old one is returned and replaces it in the
    // field at once. A failed allocation leaves that array as it was, and an array grown earlier in the same evaluation
    // stays in its field (the arrays have separate capacities), so nothing leaks and Dispose returns it.
    private static GpuBigUIntArrayView Get(ref GpuBigUIntArray field, GpuWorkspace workspace, int count, int wordsPerElement)
    {
        if (count == 0)
        {
            // An empty result borrows nothing (a zero-word allocation has no buffer)
            return new GpuBigUIntArrayView(workspace, default, 0, wordsPerElement);
        }

        if (field.Handle.IsDefault || field.Length < count)
        {
            var grown = new GpuBigUIntArray(workspace, count, wordsPerElement);
            var old = field;
            field = grown;
            old.Dispose();
        }

        return ((GpuBigUIntArrayView)field).Slice(0, count);
    }
}
