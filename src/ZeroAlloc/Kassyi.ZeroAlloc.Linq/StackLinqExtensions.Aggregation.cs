namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
    /// <summary>
    /// Applies an accumulator function over a sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">A sequence to aggregate over.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The final accumulator value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The source sequence is empty.</exception>
    public static TSource Aggregate<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, TSource, TSource> func)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(func);
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var result))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        while (enumerator.TryGetNext(out var value))
        {
            result = func(result, value);
        }
        return result;
    }

    /// <summary>
    /// Applies an accumulator function over a stack-allocated sequence.
    /// The specified seed value is used as the initial accumulator value.
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
        var count = 0;
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
        var count = 0;
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
        var count = 0;
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
}
