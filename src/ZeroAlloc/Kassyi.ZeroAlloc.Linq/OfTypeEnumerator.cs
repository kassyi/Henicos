namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Filters a sequence down to the elements of a given type, with zero heap allocations.
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying source enumerator.</typeparam>
/// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
/// <typeparam name="TResult">The type to filter the elements on.</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Evaluates elements lazily with $O(1)$ auxiliary space. A <see langword="null"/> element is
/// never of <typeparamref name="TResult"/>, so the filter also drops <see langword="null"/>.</para>
/// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
/// </remarks>
public ref struct OfTypeEnumerator<TEnumerator, TSource, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    private TEnumerator _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfTypeEnumerator{TEnumerator, TSource, TResult}"/> struct.
    /// </summary>
    /// <param name="source">The underlying enumerator providing source elements.</param>
    public OfTypeEnumerator(TEnumerator source)
    {
        _source = source;
    }

    /// <summary>
    /// Attempts to advance the enumerator to the next element of type <typeparamref name="TResult"/>.
    /// </summary>
    /// <param name="current">
    /// When this method returns <see langword="true"/>, contains the next element of type
    /// <typeparamref name="TResult"/>; otherwise, the default value of <typeparamref name="TResult"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an element of type <typeparamref name="TResult"/> is found;
    /// <see langword="false"/> if the end of the sequence was reached without finding another one.
    /// </returns>
    public bool TryGetNext(out TResult current)
    {
        while (_source.TryGetNext(out var value))
        {
            if (value is TResult result)
            {
                current = result;
                return true;
            }
        }

        // Safe fallback: default value when enumeration has reached the end
        current = default!;
        return false;
    }
}
