using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
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
    /// Computes the sum of a parallel sequence of <see cref="int"/> values. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    public static int Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        int sum = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }
        return sum;
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of custom types. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    public static TSource Sum<TEnumerator, TSource>(
        this ParallelStackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
        where TSource : IAdditionOperators<TSource, TSource, TSource>, IAdditiveIdentity<TSource, TSource>
    {
        var enumerator = source.Enumerator;
        TSource sum = TSource.AdditiveIdentity;
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
}
