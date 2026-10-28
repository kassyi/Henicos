namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The result buffers a vector expression node keeps between evaluations (design 4.6).
/// </summary>
/// <remarks>
/// <para>
/// Each array grows when an evaluation needs more elements than it holds and is reused otherwise, like the buffer of
/// <c>ArrayBufferWriter&lt;T&gt;</c>; the node lends a slice of exactly the evaluated length, valid until the next evaluation
/// or <see cref="Dispose"/>. The arrays have separate capacities: an evaluation into a caller's target uses only the
/// signs of the cache.
/// </para>
/// <para>
/// The cache owns only what it allocated; a caller's target is checked (<see cref="TargetValues"/>) and borrowed, never
/// stored.
/// </para>
/// </remarks>
internal sealed class VectorResultCache : IDisposable
{
    private GpuBigUIntArray _values;
    private GpuBigUIntArray _signs;
    private GpuBigUIntArray _exponents;
    private GpuBigUIntArray _scan;

    /// <summary>Lends <paramref name="count"/> elements of the cached values.</summary>
    public GpuBigUIntArray Values(GpuWorkspace workspace, int count, int wordsPerElement) =>
        Get(ref _values, workspace, count, wordsPerElement);

    /// <summary>Lends <paramref name="count"/> sign words.</summary>
    public GpuBigUIntArray Signs(GpuWorkspace workspace, int count) => Get(ref _signs, workspace, count, 1);

    /// <summary>Lends <paramref name="count"/> exponent words.</summary>
    public GpuBigUIntArray Exponents(GpuWorkspace workspace, int count) => Get(ref _exponents, workspace, count, 1);

    /// <summary>Lends <paramref name="count"/> words of scratch (the prefix sum of a Where).</summary>
    public GpuBigUIntArray Scan(GpuWorkspace workspace, int count) => Get(ref _scan, workspace, count, 1);

    /// <summary>
    /// Lends a signed result of <paramref name="count"/> elements, with the values from <paramref name="checkedTarget"/>
    /// (the result of <see cref="TargetValues"/>) if given.
    /// </summary>
    public GpuBigIntArray Signed(GpuWorkspace workspace, int count, int wordsPerElement, GpuBigUIntArray? checkedTarget) =>
        new(checkedTarget ?? Values(workspace, count, wordsPerElement), Signs(workspace, count));

    /// <summary>Lends a floating-point result of <paramref name="count"/> elements.</summary>
    public GpuBigFloatArray Float(GpuWorkspace workspace, int count, int wordsPerElement) =>
        new(Values(workspace, count, wordsPerElement), Exponents(workspace, count), Signs(workspace, count));

    /// <summary>
    /// Checks a caller's target and borrows its first <paramref name="count"/> elements.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The element width differs from the expression's (the kernel would write at the wrong offsets), or the target is
    /// shorter than the evaluation.
    /// </exception>
    public static GpuBigUIntArray TargetValues(GpuBigUIntArray target, int count, int wordsPerElement)
    {
        ThrowIfWrongShape(target.WordsPerElement, target.Length, count, wordsPerElement);
        return target.BorrowSlice(0, count);
    }

    /// <summary>
    /// Checks a caller's floating-point target and borrows its first <paramref name="count"/> elements.
    /// </summary>
    /// <exception cref="ArgumentException">The element width differs, or the target is too short.</exception>
    public static GpuBigFloatArray TargetFloat(GpuBigFloatArray target, int count, int wordsPerElement)
    {
        ThrowIfWrongShape(target.WordsPerElement, target.Length, count, wordsPerElement);
        return target.BorrowSlice(0, count);
    }

    /// <summary>
    /// Returns every array the cache allocated; a second call does nothing.
    /// </summary>
    public void Dispose()
    {
        // Nested like SignedBinaryExpression, so that a release that throws (a failed capture refuses the clear) does not
        // skip the others; each field is cleared even then, so that a second call frees nothing twice. A default array
        // owns nothing, so its Dispose does nothing.
        try
        {
            _values.Dispose();
        }
        finally
        {
            _values = default;
            try
            {
                _signs.Dispose();
            }
            finally
            {
                _signs = default;
                try
                {
                    _exponents.Dispose();
                }
                finally
                {
                    _exponents = default;
                    try
                    {
                        _scan.Dispose();
                    }
                    finally
                    {
                        _scan = default;
                    }
                }
            }
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
    private static GpuBigUIntArray Get(ref GpuBigUIntArray field, GpuWorkspace workspace, int count, int wordsPerElement)
    {
        if (count == 0)
        {
            // An empty result borrows nothing (a zero-word allocation has no buffer)
            return new GpuBigUIntArray(workspace, default, 0, wordsPerElement);
        }

        if (field.Handle.IsDefault || field.Length < count)
        {
            var grown = new GpuBigUIntArray(workspace, count, wordsPerElement);
            var old = field;
            field = grown;
            old.Dispose();
        }

        return field.BorrowSlice(0, count);
    }
}
