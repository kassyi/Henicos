namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// A zero-allocation marker struct indicating that the subsequent operations on the enumerator should be executed in parallel (e.g., via CUDA cooperative thread groups).
/// </summary>
/// <typeparam name="TEnumerator">The type of the underlying stack enumerator.</typeparam>
/// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
/// <param name="enumerator">The underlying stack enumerator.</param>
public readonly ref struct ParallelStackEnumerable<TEnumerator, TSource>(TEnumerator enumerator)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
{
    /// <summary>
    /// Gets the underlying stack enumerator.
    /// </summary>
    public readonly TEnumerator Enumerator = enumerator;
}
