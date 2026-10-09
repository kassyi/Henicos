using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Provides a set of <see langword="static"/> extension methods for zero-allocation, stack-based LINQ operations over arrays, spans, and struct enumerators.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Methods in this class operate via <see langword="ref struct"/> wrappers, avoiding boxing, interface dispatches, and heap allocations.</para>
/// <para><b>Execution Target:</b> Usable on CPU host (ideal for Roslyn Source Generators and high-throughput loops) and transpiled natively to C++/CUDA inside GPU kernels.</para>
/// </remarks>
public static class StackLinqExtensions
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
    /// Computes the sum of a sequence of <see cref="int"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="int"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        int sum = 0;

        if (typeof(TEnumerator) == typeof(SpanEnumerator<int>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<int>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if (Vector.IsHardwareAccelerated && span.Length >= Vector<int>.Count)
            {
                var vectors = MemoryMarshal.Cast<int, Vector<int>>(span);
                var acc = Vector<int>.Zero;
                foreach (var v in vectors)
                {
                    acc += v;
                }
                sum += Vector.Sum(acc);
                span = span.Slice(vectors.Length * Vector<int>.Count);
            }
            foreach (var val in span)
            {
                sum += val;
            }
            return sum;
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<int>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<int>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if (Vector.IsHardwareAccelerated && span.Length >= Vector<int>.Count)
            {
                var vectors = MemoryMarshal.Cast<int, Vector<int>>(span);
                var acc = Vector<int>.Zero;
                foreach (var v in vectors)
                {
                    acc += v;
                }
                sum += Vector.Sum(acc);
                span = span.Slice(vectors.Length * Vector<int>.Count);
            }
            foreach (var val in span)
            {
                sum += val;
            }
            return sum;
        }

        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Computes the sum of the sequence of <see cref="int"/> values that are obtained by invoking a transform function on each element of the input sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sum of the projected values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits the creation of a dedicated enumerator by chaining <see cref="Select{TEnumerator, TSource, TResult}(StackEnumerable{TEnumerator, TSource}, Func{TSource, TResult})"/> internally in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Sum<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, int> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).Sum();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="float"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="float"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static float Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        var enumerator = source.Enumerator;
        float sum = 0;

        if (typeof(TEnumerator) == typeof(SpanEnumerator<float>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<float>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if (Vector.IsHardwareAccelerated && span.Length >= Vector<float>.Count)
            {
                var vectors = MemoryMarshal.Cast<float, Vector<float>>(span);
                var acc = Vector<float>.Zero;
                foreach (var v in vectors)
                {
                    acc += v;
                }
                sum += Vector.Sum(acc);
                span = span.Slice(vectors.Length * Vector<float>.Count);
            }
            foreach (var val in span)
            {
                sum += val;
            }
            return sum;
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<float>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<float>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if (Vector.IsHardwareAccelerated && span.Length >= Vector<float>.Count)
            {
                var vectors = MemoryMarshal.Cast<float, Vector<float>>(span);
                var acc = Vector<float>.Zero;
                foreach (var v in vectors)
                {
                    acc += v;
                }
                sum += Vector.Sum(acc);
                span = span.Slice(vectors.Length * Vector<float>.Count);
            }
            foreach (var val in span)
            {
                sum += val;
            }
            return sum;
        }

        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Computes the sum of the sequence of <see cref="float"/> values that are obtained by invoking a transform function on each element of the input sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sum of the projected values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits the creation of a dedicated enumerator by chaining <see cref="Select{TEnumerator, TSource, TResult}(StackEnumerable{TEnumerator, TSource}, Func{TSource, TResult})"/> internally in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static float Sum<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, float> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).Sum();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="double"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="double"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static double Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct
    {
        var enumerator = source.Enumerator;
        double sum = 0;

        if (typeof(TEnumerator) == typeof(SpanEnumerator<double>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<double>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if (Vector.IsHardwareAccelerated && span.Length >= Vector<double>.Count)
            {
                var vectors = MemoryMarshal.Cast<double, Vector<double>>(span);
                var acc = Vector<double>.Zero;
                foreach (var v in vectors)
                {
                    acc += v;
                }
                sum += Vector.Sum(acc);
                span = span.Slice(vectors.Length * Vector<double>.Count);
            }
            foreach (var val in span)
            {
                sum += val;
            }
            return sum;
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<double>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<double>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if (Vector.IsHardwareAccelerated && span.Length >= Vector<double>.Count)
            {
                var vectors = MemoryMarshal.Cast<double, Vector<double>>(span);
                var acc = Vector<double>.Zero;
                foreach (var v in vectors)
                {
                    acc += v;
                }
                sum += Vector.Sum(acc);
                span = span.Slice(vectors.Length * Vector<double>.Count);
            }
            foreach (var val in span)
            {
                sum += val;
            }
            return sum;
        }

        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Computes the sum of the sequence of <see cref="double"/> values that are obtained by invoking a transform function on each element of the input sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sum of the projected values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits the creation of a dedicated enumerator by chaining <see cref="Select{TEnumerator, TSource, TResult}(StackEnumerable{TEnumerator, TSource}, Func{TSource, TResult})"/> internally in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static double Sum<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, double> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).Sum();

    /// <summary>
    /// Creates an array from a <see cref="StackEnumerable{TEnumerator, TSource}"/>.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to create an array from.</param>
    /// <returns>An array that contains the elements from the input sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Allocates only the final return array. Intermediate buffers are pooled via <see cref="System.Buffers.ArrayPool{T}.Shared"/> to eliminate heap fragmentation.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static TSource[] ToArray<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var pool = ArrayPool<TSource>.Shared;
        var buffer = pool.Rent(16);
        var count = 0;
        var enumerator = source.Enumerator;

        try
        {
            while (enumerator.TryGetNext(out var value))
            {
                if (count == buffer.Length)
                {
                    var newBuffer = pool.Rent(buffer.Length * 2);
                    Array.Copy(buffer, newBuffer, count);
                    pool.Return(buffer, clearArray: true);
                    buffer = newBuffer;
                }

                buffer[count++] = value;
            }

            var result = new TSource[count];
            Array.Copy(buffer, result, count);
            return result;
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Copies the elements of a stack-allocated sequence into a destination span without any heap allocations.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements are to be copied.</param>
    /// <param name="destination">The span that is the destination of the elements copied from the sequence.</param>
    /// <returns>The number of elements successfully copied into <paramref name="destination"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> True zero-allocation extraction. Iteration stops when <paramref name="destination"/> is full or <paramref name="source"/> is exhausted.</para>
    /// </remarks>
    public static int CopyTo<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Span<TSource> destination)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var count = 0;
        var enumerator = source.Enumerator;
        while (count < destination.Length && enumerator.TryGetNext(out var value))
        {
            destination[count++] = value;
        }

        return count;
    }

    /// <summary>
    /// Determines whether a stack-allocated sequence contains a specified element by using the default equality comparer.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence in which to locate a value.</param>
    /// <param name="value">The value to locate in the sequence.</param>
    /// <returns><see langword="true"/> if the source sequence contains an element with the specified value; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Linear search in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static bool Contains<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        TSource value)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var current))
        {
            if (EqualityComparer<TSource>.Default.Equals(current, value))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the element at a specified index in a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return an element from.</param>
    /// <param name="index">The zero-based index of the element to retrieve.</param>
    /// <returns>The element at the specified position in the source sequence.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than 0 or greater than or equal to the number of elements in <paramref name="source"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ indexing for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource ElementAt<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        int index)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        
        if (typeof(TEnumerator) == typeof(SpanEnumerator<TSource>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<TSource>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if ((uint)index < (uint)span.Length)
            {
                return span[index];
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<TSource>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<TSource>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if ((uint)index < (uint)span.Length)
            {
                return span[index];
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int count = 0;
        while (enumerator.TryGetNext(out var current))
        {
            if (count == index)
            {
                return current;
            }
            count++;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Returns the element at a specified index in a stack-allocated sequence or a default value if the index is out of range.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return an element from.</param>
    /// <param name="index">The zero-based index of the element to retrieve.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if <paramref name="index"/> is outside the bounds of <paramref name="source"/>; otherwise, the element at the specified position.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ indexing for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource? ElementAtOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        int index)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        
        if (typeof(TEnumerator) == typeof(SpanEnumerator<TSource>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<TSource>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if ((uint)index < (uint)span.Length)
            {
                return span[index];
            }
            return default;
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<TSource>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<TSource>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if ((uint)index < (uint)span.Length)
            {
                return span[index];
            }
            return default;
        }

        int count = 0;
        while (enumerator.TryGetNext(out var current))
        {
            if (count == index)
            {
                return current;
            }
            count++;
        }
        return default;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence, and throws an exception if there is not exactly one element in the sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the single element of.</param>
    /// <returns>The single element of the input sequence.</returns>
    /// <exception cref="InvalidOperationException">The input sequence contains no elements or more than one element.</exception>
    public static TSource Single<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var result))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        if (enumerator.TryGetNext(out _))
        {
            throw new InvalidOperationException("Sequence contains more than one element");
        }
        return result;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence that satisfies a specified condition, and throws an exception if more than one such element exists.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return a single element from.</param>
    /// <param name="predicate">A function to test an element for a condition.</param>
    /// <returns>The single element of the input sequence that satisfies the condition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No element satisfies the condition in <paramref name="predicate"/>, or more than one element satisfies the condition.</exception>
    public static TSource Single<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        TSource result = default!;
        bool found = false;
        while (enumerator.TryGetNext(out var current))
        {
            if (predicate(current))
            {
                if (found)
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }
                result = current;
                found = true;
            }
        }
        if (!found)
        {
            throw new InvalidOperationException("Sequence contains no matching element");
        }
        return result;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence, or a default value if the sequence is empty; this method throws an exception if there is more than one element in the sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the single element of.</param>
    /// <returns>The single element of the input sequence, or default value if the sequence contains no elements.</returns>
    /// <exception cref="InvalidOperationException">The input sequence contains more than one element.</exception>
    public static TSource? SingleOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var result))
        {
            return default;
        }
        if (enumerator.TryGetNext(out _))
        {
            throw new InvalidOperationException("Sequence contains more than one element");
        }
        return result;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence that satisfies a specified condition, or a default value if no such element exists; this method throws an exception if more than one element satisfies the condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return a single element from.</param>
    /// <param name="predicate">A function to test an element for a condition.</param>
    /// <returns>The single element of the input sequence that satisfies the condition, or default value if no such element is found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">More than one element satisfies the condition in <paramref name="predicate"/>.</exception>
    public static TSource? SingleOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        TSource result = default!;
        bool found = false;
        while (enumerator.TryGetNext(out var current))
        {
            if (predicate(current))
            {
                if (found)
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }
                result = current;
                found = true;
            }
        }
        if (!found)
        {
            return default;
        }
        return result;
    }

    /// <summary>
    /// Returns the last element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the last element of.</param>
    /// <returns>The value at the last position in the source sequence.</returns>
    /// <exception cref="InvalidOperationException">The source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource Last<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        
        if (typeof(TEnumerator) == typeof(SpanEnumerator<TSource>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<TSource>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if (span.Length > 0)
            {
                return span[^1];
            }
            throw new InvalidOperationException("Sequence contains no elements");
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<TSource>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<TSource>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if (span.Length > 0)
            {
                return span[^1];
            }
            throw new InvalidOperationException("Sequence contains no elements");
        }

        bool found = false;
        TSource result = default!;
        while (enumerator.TryGetNext(out var current))
        {
            result = current;
            found = true;
        }
        if (!found)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        return result;
    }

    /// <summary>
    /// Returns the last element of a stack-allocated sequence, or a default value if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the last element of.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if the source sequence is empty; otherwise, the last element in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource? LastOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        
        if (typeof(TEnumerator) == typeof(SpanEnumerator<TSource>))
        {
            ref var spanEnum = ref Unsafe.As<TEnumerator, SpanEnumerator<TSource>>(ref enumerator);
            var span = spanEnum.GetRemainingSpanAndAdvance();
            if (span.Length > 0)
            {
                return span[^1];
            }
            return default;
        }
        else if (typeof(TEnumerator) == typeof(ArrayEnumerator<TSource>))
        {
            ref var arrayEnum = ref Unsafe.As<TEnumerator, ArrayEnumerator<TSource>>(ref enumerator);
            var span = arrayEnum.GetRemainingSpanAndAdvance();
            if (span.Length > 0)
            {
                return span[^1];
            }
            return default;
        }

        bool found = false;
        TSource result = default!;
        while (enumerator.TryGetNext(out var current))
        {
            result = current;
            found = true;
        }
        if (!found)
        {
            return default;
        }
        return result;
    }


    /// <summary>
    /// Returns elements from a stack-allocated sequence as long as a specified condition is true.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that occur before the element at which the test no longer passes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<TakeWhileEnumerator<TEnumerator, TSource>, TSource> TakeWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<TakeWhileEnumerator<TEnumerator, TSource>, TSource>(
            new TakeWhileEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Returns elements from a stack-allocated sequence as long as a specified condition is true. The element's index is used in the logic of the predicate function.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each source element for a condition; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that occur before the element at which the test no longer passes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<TakeWhileAtEnumerator<TEnumerator, TSource>, TSource> TakeWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<TakeWhileAtEnumerator<TEnumerator, TSource>, TSource>(
            new TakeWhileAtEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence starting at the first element that does not pass the test.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<SkipWhileEnumerator<TEnumerator, TSource>, TSource> SkipWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<SkipWhileEnumerator<TEnumerator, TSource>, TSource>(
            new SkipWhileEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements. The element's index is used in the logic of the predicate function.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each source element for a condition; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence starting at the first element that does not pass the test.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<SkipWhileAtEnumerator<TEnumerator, TSource>, TSource> SkipWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<SkipWhileAtEnumerator<TEnumerator, TSource>, TSource>(
            new SkipWhileAtEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Returns a specified number of contiguous elements from the start of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return elements from.</param>
    /// <param name="count">The number of elements to return.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains the specified number of elements from the start of the input sequence.</returns>
    public static StackEnumerable<TakeEnumerator<TEnumerator, TSource>, TSource> Take<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, int count)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        return new StackEnumerable<TakeEnumerator<TEnumerator, TSource>, TSource>(
            new TakeEnumerator<TEnumerator, TSource>(source.Enumerator, count));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the last <paramref name="count"/> elements from <paramref name="source"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source span sequence to return elements from.</param>
    /// <param name="count">The number of elements to take from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing the last <paramref name="count"/> elements.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> TakeLast<TSource>(
        this StackEnumerable<SpanEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int takeCount = Math.Min(count, span.Length);
        if (takeCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(span.Length - takeCount, takeCount)));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the last <paramref name="count"/> elements from <paramref name="source"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source array sequence to return elements from.</param>
    /// <param name="count">The number of elements to take from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing the last <paramref name="count"/> elements.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> TakeLast<TSource>(
        this StackEnumerable<ArrayEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int takeCount = Math.Min(count, span.Length);
        if (takeCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(span.Length - takeCount, takeCount)));
    }

    /// <summary>
    /// Bypasses a specified number of elements in a stack-allocated sequence and then returns the remaining elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return elements from.</param>
    /// <param name="count">The number of elements to skip before returning the remaining elements.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements that occur after the specified index in the input sequence.</returns>
    public static StackEnumerable<SkipEnumerator<TEnumerator, TSource>, TSource> Skip<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, int count)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        return new StackEnumerable<SkipEnumerator<TEnumerator, TSource>, TSource>(
            new SkipEnumerator<TEnumerator, TSource>(source.Enumerator, count));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the elements from <paramref name="source"/> with the last <paramref name="count"/> elements omitted.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source span sequence to omit elements from.</param>
    /// <param name="count">The number of elements to omit from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing elements with the last <paramref name="count"/> elements omitted.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> SkipLast<TSource>(
        this StackEnumerable<SpanEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int skipCount = Math.Min(count, span.Length);
        if (span.Length - skipCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(0, span.Length - skipCount)));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the elements from <paramref name="source"/> with the last <paramref name="count"/> elements omitted.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source array sequence to omit elements from.</param>
    /// <param name="count">The number of elements to omit from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing elements with the last <paramref name="count"/> elements omitted.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> SkipLast<TSource>(
        this StackEnumerable<ArrayEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int skipCount = Math.Min(count, span.Length);
        if (span.Length - skipCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(0, span.Length - skipCount)));
    }

    /// <summary>
    /// Applies an accumulator function over a stack-allocated sequence. The specified seed value is used as the initial accumulator value.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <typeparam name="TAccumulate">The type of the accumulator value.</typeparam>
    /// <param name="source">A stack-allocated sequence to aggregate over.</param>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The final accumulator value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is <see langword="null"/>.</exception>
    public static TAccumulate Aggregate<TEnumerator, TSource, TAccumulate>(
        this StackEnumerable<TEnumerator, TSource> source,
        TAccumulate seed,
        Func<TAccumulate, TSource, TAccumulate> func)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = seed;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            result = func(result, value);
        }
        return result;
    }

    /// <summary>
    /// Returns the maximum value in a stack-allocated sequence of <see cref="int"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="int"/> values to determine the maximum value of.</param>
    /// <returns>The maximum value in the sequence.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static int Max<TEnumerator>(this StackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var max))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var value))
        {
            if (value > max)
            {
                max = value;
            }
        }
        return max;
    }

    /// <summary>
    /// Returns the minimum value in a stack-allocated sequence of <see cref="int"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="int"/> values to determine the minimum value of.</param>
    /// <returns>The minimum value in the sequence.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static int Min<TEnumerator>(this StackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var min))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var value))
        {
            if (value < min)
            {
                min = value;
            }
        }
        return min;
    }

    /// <summary>
    /// Computes the average of a stack-allocated sequence of <see cref="int"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="int"/> values to calculate the average of.</param>
    /// <returns>The average of the sequence of values.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static double Average<TEnumerator>(this StackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        long sum = 0;
        int count = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
            count++;
        }
        if (count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        return (double)sum / count;
    }

    /// <summary>
    /// Returns the maximum value in a stack-allocated sequence of <see cref="float"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="float"/> values to determine the maximum value of.</param>
    /// <returns>The maximum value in the sequence.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static float Max<TEnumerator>(this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var max))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var value))
        {
            if (value > max)
            {
                max = value;
            }
        }
        return max;
    }

    /// <summary>
    /// Returns the minimum value in a stack-allocated sequence of <see cref="float"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="float"/> values to determine the minimum value of.</param>
    /// <returns>The minimum value in the sequence.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static float Min<TEnumerator>(this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var min))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var value))
        {
            if (value < min)
            {
                min = value;
            }
        }
        return min;
    }

    /// <summary>
    /// Computes the average of a stack-allocated sequence of <see cref="float"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="float"/> values to calculate the average of.</param>
    /// <returns>The average of the sequence of values.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static float Average<TEnumerator>(this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        var enumerator = source.Enumerator;
        double sum = 0;
        int count = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
            count++;
        }
        if (count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        return (float)(sum / count);
    }

    /// <summary>
    /// Returns the maximum value in a stack-allocated sequence of <see cref="double"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="double"/> values to determine the maximum value of.</param>
    /// <returns>The maximum value in the sequence.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static double Max<TEnumerator>(this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var max))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var value))
        {
            if (value > max)
            {
                max = value;
            }
        }
        return max;
    }

    /// <summary>
    /// Returns the minimum value in a stack-allocated sequence of <see cref="double"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="double"/> values to determine the minimum value of.</param>
    /// <returns>The minimum value in the sequence.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static double Min<TEnumerator>(this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var min))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var value))
        {
            if (value < min)
            {
                min = value;
            }
        }
        return min;
    }

    /// <summary>
    /// Computes the average of a stack-allocated sequence of <see cref="double"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="double"/> values to calculate the average of.</param>
    /// <returns>The average of the sequence of values.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains no elements.</exception>
    public static double Average<TEnumerator>(this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct
    {
        var enumerator = source.Enumerator;
        double sum = 0;
        int count = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
            count++;
        }
        if (count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        return sum / count;
    }


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
