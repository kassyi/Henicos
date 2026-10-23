using System.Runtime.InteropServices;

namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Provides a set of <see langword="static"/> extension methods for zero-allocation, stack-based LINQ operations over arrays, spans, and struct enumerators.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Methods in this class operate via <see langword="ref struct"/> wrappers, avoiding boxing, interface dispatches, and heap allocations.</para>
/// <para><b>Execution Target:</b> Usable on CPU host (ideal for Roslyn Source Generators and high-throughput loops) and transpiled natively to C++/CUDA inside GPU kernels.</para>
/// </remarks>
public static partial class StackLinqExtensions
{
    /// <summary>
    /// Wraps a managed array into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the array.</typeparam>
    /// <param name="source">The source array to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping an <see cref="ArrayEnumerator{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<ArrayEnumerator<T>, T> AsStackEnumerable<T>(this T[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(new ArrayEnumerator<T>(source));
    }

    /// <summary>
    /// Wraps a <see cref="System.Collections.Generic.List{T}"/> into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the list.</typeparam>
    /// <param name="source">The source list to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping a <see cref="SpanEnumerator{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations by accessing the underlying span.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this List<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(new SpanEnumerator<T>(CollectionsMarshal.AsSpan(source)));
    }

    /// <summary>
    /// Wraps a <see cref="Span{T}"/> into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the span.</typeparam>
    /// <param name="source">The source span to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping a <see cref="SpanEnumerator{T}"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this Span<T> source) =>
        new(new SpanEnumerator<T>(source));

    /// <summary>
    /// Wraps a <see cref="ReadOnlySpan{T}"/> into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the span.</typeparam>
    /// <param name="source">The source read-only span to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping a <see cref="SpanEnumerator{T}"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this ReadOnlySpan<T> source) =>
        new(new SpanEnumerator<T>(source));

    /// <summary>
    /// Instructs the compiler to execute the subsequent reduction operations in parallel on the GPU.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to parallelize.</param>
    /// <returns>A zero-allocation marker struct <see cref="ParallelStackEnumerable{TEnumerator, TSource}"/>.</returns>
    public static ParallelStackEnumerable<TEnumerator, TSource> AsParallel<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        new(source.Enumerator);

    // -----------------------------------------------------------------------------------------
    // BANNED LINQ OPERATIONS (DO NOT IMPLEMENT)
    // -----------------------------------------------------------------------------------------
    // The following operations are deliberately NOT implemented in StackLinq. They violate
    // the core principles of zero-allocation, thread-local SIMT execution on GPUs:
    //
    // A. Requires Dynamic Heap Allocation (Out of Memory / GC Constraints)
    //    - ToList<T>()
    //    - ToDictionary<TKey, TValue>(...)
    //    - ToHashSet<T>(...)
    //    - ToLookup<TKey, TElement>(...)
    //
    // B. Requires Dynamic State Allocation / Hashing (Bucket Construction)
    //    - Distinct(), DistinctBy(...)
    //    - Union(), UnionBy(...)
    //    - Except(), ExceptBy(...)
    //    - Intersect(), IntersectBy(...)
    //    - GroupBy(...)
    //    - Join(), GroupJoin(...)
    //
    // C. Requires Global Synchronization / Full Buffering (Breaks Thread Pipeline)
    //    - OrderBy(), OrderByDescending()
    //    - ThenBy(), ThenByDescending()
    //    - Order(), OrderDescending()
    //    - Chunk(int)
    //    (Note: If you need sorting or grouping, process the array directly on the CPU
    //     before execution, or use dedicated array-level operations like GpuArray.Sort()).
    //
    // D. Requires Dynamic Dispatch / Reflection (RTTI)
    //    - Cast<TResult>()
    //    - OfType<TResult>()
    // -----------------------------------------------------------------------------------------
}
