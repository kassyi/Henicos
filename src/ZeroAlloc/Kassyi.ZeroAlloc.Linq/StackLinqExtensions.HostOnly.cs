namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
    // Host-only operations: add to a caller-owned collection, run an action per element, flatten, and searches that
    // report whether they found anything. A G# kernel reports GSH0003 for them.

    /// <summary>
    /// Adds every element of a stack-allocated sequence to a collection the caller owns.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements are added.</param>
    /// <param name="destination">The collection that receives the elements.</param>
    /// <returns>The number of elements added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Allocates nothing itself; growing <paramref name="destination"/> is the caller's concern, unlike <c>ToList</c>, which creates the collection.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static int AddTo<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        ICollection<TSource> destination)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(destination);
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            destination.Add(value);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Adds the elements that satisfy a condition to a collection the caller owns.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements are tested.</param>
    /// <param name="destination">The collection that receives the matching elements.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The number of elements added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> or <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static int AddIf<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        ICollection<TSource> destination,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                destination.Add(value);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Adds the elements that satisfy a condition that receives a caller-supplied state to a collection the caller owns.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence whose elements are tested.</param>
    /// <param name="destination">The collection that receives the matching elements.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns>The number of elements added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> or <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Passing the outer value as <paramref name="state"/> lets <paramref name="predicate"/> be a <see langword="static"/> lambda. With a <see cref="HashSet{T}"/> as state and <c>seen.Add(x)</c> as the predicate, it keeps the first of each value in order.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static int AddIf<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        ICollection<TSource> destination,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                destination.Add(value);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Runs an action on every element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements are visited.</param>
    /// <param name="action">The action to run on each element.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static void Do<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Action<TSource> action)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            action(value);
        }
    }

    /// <summary>
    /// Runs an action that receives a caller-supplied state on every element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="action"/>.</typeparam>
    /// <param name="source">The sequence whose elements are visited.</param>
    /// <param name="state">The value passed to every call of <paramref name="action"/>.</param>
    /// <param name="action">The action to run on each element; the second parameter is <paramref name="state"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static void Do<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Action<TSource, TState> action)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            action(value, state);
        }
    }

    /// <summary>
    /// Runs an action on the elements of a stack-allocated sequence that satisfy a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements are tested.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="action">The action to run on each matching element.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static void DoIf<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate,
        Action<TSource> action)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                action(value);
            }
        }
    }

    /// <summary>
    /// Runs an action on the elements that satisfy a condition; both receive a caller-supplied state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/> and <paramref name="action"/>.</typeparam>
    /// <param name="source">The sequence whose elements are tested.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/> and <paramref name="action"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <param name="action">The action to run on each matching element; the second parameter is <paramref name="state"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static void DoIf<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate,
        Action<TSource, TState> action)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                action(value, state);
            }
        }
    }

    /// <summary>
    /// Projects each element to an inner stack-allocated sequence and flattens the inner sequences into one.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <typeparam name="TInnerEnumerator">The type of the enumerator of each inner sequence.</typeparam>
    /// <typeparam name="TResult">The type of the elements in the inner sequences.</typeparam>
    /// <param name="source">The sequence whose elements are projected.</param>
    /// <param name="selector">A function that returns the inner sequence of an element, such as <c>static x => x.Items.AsStackEnumerable()</c>.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TResult}"/> over the elements of every inner sequence, in order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static StackEnumerable<SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult>, TResult>
        SelectMany<TEnumerator, TSource, TInnerEnumerator, TResult>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, StackEnumerable<TInnerEnumerator, TResult>> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
        where TInnerEnumerator : struct, IStackEnumerator<TResult>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new StackEnumerable<SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult>, TResult>(
            new SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult>(source.Enumerator, selector));
    }

    /// <summary>
    /// Finds the first element that satisfies a condition and reports whether one was found.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to search.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="value">The first matching element, or the default value of <typeparamref name="TSource"/> when there is none.</param>
    /// <returns><see langword="true"/> if an element satisfies the condition; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Remarks:</b> Unlike <c>FirstOrDefault</c>, it tells a match equal to the default value (a <c>0</c>, an empty <see cref="KeyValuePair{TKey, TValue}"/>) from no match.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static bool TryFirst<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate,
        [MaybeNullWhen(false)] out TSource value)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var current))
        {
            if (predicate(current))
            {
                value = current;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Finds the first element that satisfies a condition that receives a caller-supplied state, and reports whether one
    /// was found.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence to search.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <param name="value">The first matching element, or the default value of <typeparamref name="TSource"/> when there is none.</param>
    /// <returns><see langword="true"/> if an element satisfies the condition; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static bool TryFirst<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate,
        [MaybeNullWhen(false)] out TSource value)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var current))
        {
            if (predicate(current, state))
            {
                value = current;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns the zero-based position of the first element that satisfies a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to search.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The position of the first matching element, or <c>-1</c> when there is none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static int IndexOf<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var index = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>
    /// Returns the zero-based position of the first element that satisfies a condition that receives a caller-supplied
    /// state.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TState">The type of the state passed to <paramref name="predicate"/>.</typeparam>
    /// <param name="source">The sequence to search.</param>
    /// <param name="state">The value passed to every call of <paramref name="predicate"/>.</param>
    /// <param name="predicate">A function to test each element for a condition; the second parameter is <paramref name="state"/>.</param>
    /// <returns>The position of the first matching element, or <c>-1</c> when there is none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static int IndexOf<TEnumerator, TSource, TState>(
        this StackEnumerable<TEnumerator, TSource> source,
        TState state,
        Func<TSource, TState, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var index = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value, state))
            {
                return index;
            }

            index++;
        }

        return -1;
    }
}
