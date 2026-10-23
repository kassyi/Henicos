namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
    /// <summary>
    /// Filters a stack-allocated sequence of values based on a predicate.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to filter.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<WhereEnumerator<TEnumerator, TSource>, TSource> Where<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<WhereEnumerator<TEnumerator, TSource>, TSource>(
            new WhereEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Filters a stack-allocated sequence of values based on a predicate. Each element's index is used in the logic of the predicate function.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to filter.</param>
    /// <param name="predicate">A function to test each source element for a condition; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<WhereAtEnumerator<TEnumerator, TSource>, TSource> Where<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, int, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<WhereAtEnumerator<TEnumerator, TSource>, TSource>(
            new WhereAtEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Projects each element of a stack-allocated sequence into a new form.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="selector"/>.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TResult}"/> whose elements are the result of invoking the transform function on each element of <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SelectEnumerator<TEnumerator, TSource, TResult>, TResult> Select<TEnumerator, TSource,
        TResult>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, TResult> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new StackEnumerable<SelectEnumerator<TEnumerator, TSource, TResult>, TResult>(
            new SelectEnumerator<TEnumerator, TSource, TResult>(source.Enumerator, selector));
    }

    /// <summary>
    /// Projects each element of a stack-allocated sequence into a new form by incorporating the element's index.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="selector"/>.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each source element; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TResult}"/> whose elements are the result of invoking the transform function on each element of <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SelectAtEnumerator<TEnumerator, TSource, TResult>, TResult> Select<TEnumerator, TSource,
        TResult>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, int, TResult> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new StackEnumerable<SelectAtEnumerator<TEnumerator, TSource, TResult>, TResult>(
            new SelectAtEnumerator<TEnumerator, TSource, TResult>(source.Enumerator, selector));
    }

    /// <summary>
    /// Filters a stack-allocated sequence of values based on a predicate that receives a caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The source sequence to filter.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily. Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static StackEnumerable<WhereWithStateEnumerator<TEnumerator, TSource, TState>, TSource> Where<TEnumerator,
        TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<WhereWithStateEnumerator<TEnumerator, TSource, TState>, TSource>(
            new WhereWithStateEnumerator<TEnumerator, TSource, TState>(source.Enumerator, state, predicate));
    }

    /// <summary>
    /// Projects each element of a stack-allocated sequence into a new form with a selector that receives a
    /// caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="selector"/>.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="selector"/>.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="state">The value passed to every call of <paramref name="selector"/>.</param>
    /// <param name="selector">A transform function to apply to each element; the second parameter is <paramref name="state"/>.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TResult}"/> whose elements are the result of invoking the transform function on each element of <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily. Passing the outer value as <paramref name="state"/> lets <paramref name="selector"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static StackEnumerable<SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>, TResult> Select<
        TEnumerator, TSource, TState, TResult>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, TResult> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new StackEnumerable<SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>, TResult>(
            new SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>(source.Enumerator, state, selector));
    }
}
