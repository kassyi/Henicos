namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
    /// <summary>
    /// Returns the number of elements in a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence that contains elements to be counted.</param>
    /// <returns>The number of elements in the input sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Count<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Returns a number that represents how many elements in the specified stack-allocated sequence satisfy a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence that contains elements to be counted.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A number that represents how many elements in the sequence satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Count<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns a number that represents how many elements in the specified stack-allocated sequence satisfy a condition. Each element's index is used in the logic of the predicate function.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence that contains elements to be counted.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A number that represents how many elements in the sequence satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Count<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, int, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var count = 0;
        var index = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, index++))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Determines whether a stack-allocated sequence contains any elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to check for non-emptiness.</param>
    /// <returns><see langword="true"/> if the source sequence contains any elements; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits on the first element ($O(1)$) with zero heap allocations.</para>
    /// </remarks>
    public static bool Any<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return enumerator.TryGetNext(out _);
    }

    /// <summary>
    /// Determines whether any element of a stack-allocated sequence satisfies a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements to apply the predicate to.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><see langword="true"/> if any element satisfies the condition; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first matching element with zero heap allocations.</para>
    /// </remarks>
    public static bool Any<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether all elements of a stack-allocated sequence satisfy a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements to apply the predicate to.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><see langword="true"/> if every element of the source sequence passes the test in the specified predicate, or if the sequence is empty; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first non-matching element with zero heap allocations.</para>
    /// </remarks>
    public static bool All<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (!predicate(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the first element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return the first element of.</param>
    /// <returns>The first element in the sequence.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static TSource First<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (enumerator.TryGetNext(out var value))
        {
            return value;
        }

        throw new InvalidOperationException("Sequence contains no elements");
    }

    /// <summary>
    /// Returns the first element in a stack-allocated sequence that satisfies a specified condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return an element from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The first element in the sequence that matches the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no element satisfies the condition, or if the source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first match with zero heap allocations.</para>
    /// </remarks>
    public static TSource First<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException("Sequence contains no matching element");
    }

    /// <summary>
    /// Returns the first element of a stack-allocated sequence, or a default value if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return the first element of.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if <paramref name="source"/> is empty; otherwise, the first element.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static TSource? FirstOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (enumerator.TryGetNext(out var value))
        {
            return value;
        }

        return default;
    }

    /// <summary>
    /// Returns the first element of the sequence that satisfies a condition, or a default value if no such element is found.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return an element from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if <paramref name="source"/> is empty or if no element passes the test; otherwise, the first element that passes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first matching element with zero heap allocations.</para>
    /// </remarks>
    public static TSource? FirstOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return value;
            }
        }

        return default;
    }

    /// <summary>
    /// Returns a number that represents how many elements satisfy a condition that receives a caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence that contains elements to be counted.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns>A number that represents how many elements in the sequence satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static int Count<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Determines whether any element satisfies a condition that receives a caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence whose elements to apply the predicate to.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns><see langword="true"/> if any element satisfies the condition; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first matching element. Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static bool Any<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether all elements satisfy a condition that receives a caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence whose elements to apply the predicate to.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns><see langword="true"/> if every element passes the test, or if the sequence is empty; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first non-matching element. Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static bool All<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (!predicate(value, state))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the first element that satisfies a condition that receives a caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence to return an element from.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns>The first element in the sequence that matches the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no element satisfies the condition, or if the source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first match. Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static TSource First<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                return value;
            }
        }

        throw new InvalidOperationException("Sequence contains no matching element");
    }

    /// <summary>
    /// Returns the first element that satisfies a condition that receives a caller-supplied state, or a default value if
    /// no such element is found.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence to return an element from.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if no element passes the test; otherwise, the first element that passes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first match. Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda, so no closure is allocated.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static TSource? FirstOrDefault<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                return value;
            }
        }

        return default;
    }
}
