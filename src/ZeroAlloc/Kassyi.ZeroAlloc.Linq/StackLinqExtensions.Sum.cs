using System.Numerics;
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
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="int"/>.</exception>
    /// <remarks>
    /// <para>The elements are added in a wider type, and the sum is range-checked once at the end, so the result does
    /// not depend on the order of the elements: an input whose running sum leaves the range and comes back
    /// (<c>[int.MaxValue, 1, -1]</c>) gives <see cref="int.MaxValue"/>, where <c>System.Linq.Enumerable.Sum</c> throws at
    /// the element that overflows. The device paths give the same value and throw the same exception.</para>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (TryTakeRemainingSpan<TEnumerator, int>(ref enumerator, out var span))
        {
            return NarrowSum(SumWidened(span));
        }

        return NarrowSum(SumExactInt32<TEnumerator>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="int"/> values. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="int"/>.</exception>
    /// <remarks>The sum is exact and range-checked once, as the sequential <c>Sum</c>.</remarks>
    public static int Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return NarrowSum(SumExactInt32<TEnumerator>(ref enumerator));
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
        var sum = TSource.AdditiveIdentity;
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
    /// <para>As <c>System.Linq</c>, the elements are added in sequence order in a <see cref="double"/> accumulator,
    /// which is rounded to <see cref="float"/> once at the end.</para>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static float Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        var enumerator = source.Enumerator;
        double sum = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return (float)sum;
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="float"/> values. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of <see cref="float"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// As the sequential <c>Sum</c> and as PLINQ, the elements are added in a <see cref="double"/> accumulator, which
    /// is rounded to <see cref="float"/> once at the end; <c>AsParallel()</c> does not trade that accuracy for speed.
    /// </remarks>
    public static float Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        var enumerator = source.Enumerator;
        double sum = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return (float)sum;
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
    /// <para>As <c>System.Linq</c>, the elements are added in sequence order.</para>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static double Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct
    {
        var enumerator = source.Enumerator;
        double sum = 0;
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
    /// Computes the sum of a sequence of <see cref="float"/> values in sequential element order without SIMD reassociation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="float"/> values to calculate the sum of.</param>
    /// <returns>The sequential sum of the values in the sequence.</returns>
    /// <remarks>The same as <c>Sum</c>, which adds in sequence order too.</remarks>
    public static float SumSequential<TEnumerator>(
        this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct =>
        source.Sum();

    /// <summary>
    /// Computes the sum of the sequence of <see cref="float"/> values obtained by invoking a transform function on each element of the input sequence, in sequential element order without SIMD reassociation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sequential sum of the projected values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    public static float SumSequential<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, float> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).SumSequential();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="double"/> values in sequential element order without SIMD reassociation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="double"/> values to calculate the sum of.</param>
    /// <returns>The sequential sum of the values in the sequence.</returns>
    /// <remarks>The same as <c>Sum</c>, which adds in sequence order too.</remarks>
    public static double SumSequential<TEnumerator>(
        this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct =>
        source.Sum();

    /// <summary>
    /// Computes the sum of the sequence of <see cref="double"/> values obtained by invoking a transform function on each element of the input sequence, in sequential element order without SIMD reassociation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sequential sum of the projected values.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    public static double SumSequential<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, double> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).SumSequential();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="uint"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="uint"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="uint"/>.</exception>
    /// <remarks>The sum is exact and range-checked once at the end, as <c>Sum</c> of <see cref="int"/> values.</remarks>
    public static uint Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, uint> source)
        where TEnumerator : struct, IStackEnumerator<uint>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (TryTakeRemainingSpan<TEnumerator, uint>(ref enumerator, out var span))
        {
            return NarrowSum(SumWidened(span));
        }

        return NarrowSum(SumExactUInt32<TEnumerator>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="uint"/> values. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="uint"/>.</exception>
    /// <remarks>The sum is exact and range-checked once, as the sequential <c>Sum</c>.</remarks>
    public static uint Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, uint> source)
        where TEnumerator : struct, IStackEnumerator<uint>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return NarrowSum(SumExactUInt32<TEnumerator>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of the sequence of <see cref="uint"/> values that are obtained by invoking a transform function on each element of the input sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sum of the projected values.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="uint"/>.</exception>
    public static uint Sum<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, uint> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).Sum();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="long"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="long"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="long"/>.</exception>
    /// <remarks>The sum is exact and range-checked once at the end, as <c>Sum</c> of <see cref="int"/> values.</remarks>
    public static long Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, long> source)
        where TEnumerator : struct, IStackEnumerator<long>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return NarrowSum<long>(SumExact<TEnumerator, long>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="long"/> values. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="long"/>.</exception>
    /// <remarks>The sum is exact and range-checked once, as the sequential <c>Sum</c>.</remarks>
    public static long Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, long> source)
        where TEnumerator : struct, IStackEnumerator<long>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return NarrowSum<long>(SumExact<TEnumerator, long>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of the sequence of <see cref="long"/> values that are obtained by invoking a transform function on each element of the input sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sum of the projected values.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="long"/>.</exception>
    public static long Sum<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, long> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).Sum();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="ulong"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="ulong"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="ulong"/>.</exception>
    /// <remarks>The sum is exact and range-checked once at the end, as <c>Sum</c> of <see cref="int"/> values.</remarks>
    public static ulong Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, ulong> source)
        where TEnumerator : struct, IStackEnumerator<ulong>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return NarrowSum<ulong>(SumExact<TEnumerator, ulong>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="ulong"/> values. CPU fallback implementation.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="ulong"/>.</exception>
    /// <remarks>The sum is exact and range-checked once, as the sequential <c>Sum</c>.</remarks>
    public static ulong Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, ulong> source)
        where TEnumerator : struct, IStackEnumerator<ulong>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return NarrowSum<ulong>(SumExact<TEnumerator, ulong>(ref enumerator));
    }

    /// <summary>
    /// Computes the sum of the sequence of <see cref="ulong"/> values that are obtained by invoking a transform function on each element of the input sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>The sum of the projected values.</returns>
    /// <exception cref="OverflowException">The exact sum of the values does not fit in <see cref="ulong"/>.</exception>
    public static ulong Sum<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, ulong> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Select(selector).Sum();

    // The exact sum of int elements: a sequence has fewer than 2^31 elements, so 64 bits cannot overflow
    private static long SumExactInt32<TEnumerator>(ref TEnumerator enumerator)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        long sum = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    private static ulong SumExactUInt32<TEnumerator>(ref TEnumerator enumerator)
        where TEnumerator : struct, IStackEnumerator<uint>, allows ref struct
    {
        ulong sum = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    // The exact sum of the elements left in the enumerator, in 128 bits: no sequence has enough elements to leave it
    private static Int128 SumExact<TEnumerator, T>(ref TEnumerator enumerator)
        where TEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : IBinaryInteger<T>
    {
        Int128 sum = 0;
        while (enumerator.TryGetNext(out var value))
        {
            sum += Int128.CreateChecked(value);
        }

        return sum;
    }

    // [WHY] The exact sum is range-checked once, so the result does not depend on the order of the additions (the
    // block and grid partial sums of the device add in another order); System.Linq checks every addition
    private static T NarrowSum<T>(Int128 sum)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        if (sum < Int128.CreateChecked(T.MinValue) || sum > Int128.CreateChecked(T.MaxValue))
        {
            ThrowSumOverflow();
        }

        return T.CreateChecked(sum);
    }

    private static int NarrowSum(long sum)
    {
        if (sum is < int.MinValue or > int.MaxValue)
        {
            ThrowSumOverflow();
        }

        return (int)sum;
    }

    private static uint NarrowSum(ulong sum)
    {
        if (sum > uint.MaxValue)
        {
            ThrowSumOverflow();
        }

        return (uint)sum;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowSumOverflow() =>
        throw new OverflowException("Arithmetic operation resulted in an overflow.");

    // The SIMD path of the int sum: whole vectors first, widened to 64 bits, then the elements left over. The sum is
    // exact (a span has fewer than 2^31 elements), so the order does not change the result
    private static long SumWidened(ReadOnlySpan<int> span)
    {
        long sum = 0;
        if (Vector.IsHardwareAccelerated && span.Length >= Vector<int>.Count)
        {
            var vectors = MemoryMarshal.Cast<int, Vector<int>>(span);
            var low = Vector<long>.Zero;
            var high = Vector<long>.Zero;
            foreach (var v in vectors)
            {
                Vector.Widen(v, out var l, out var h);
                low += l;
                high += h;
            }

            sum = Vector.Sum(low) + Vector.Sum(high);
            span = span.Slice(vectors.Length * Vector<int>.Count);
        }

        foreach (var val in span)
        {
            sum += val;
        }

        return sum;
    }

    // The same for uint, in 64 unsigned bits
    private static ulong SumWidened(ReadOnlySpan<uint> span)
    {
        ulong sum = 0;
        if (Vector.IsHardwareAccelerated && span.Length >= Vector<uint>.Count)
        {
            var vectors = MemoryMarshal.Cast<uint, Vector<uint>>(span);
            var low = Vector<ulong>.Zero;
            var high = Vector<ulong>.Zero;
            foreach (var v in vectors)
            {
                Vector.Widen(v, out var l, out var h);
                low += l;
                high += h;
            }

            sum = Vector.Sum(low) + Vector.Sum(high);
            span = span.Slice(vectors.Length * Vector<uint>.Count);
        }

        foreach (var val in span)
        {
            sum += val;
        }

        return sum;
    }
}
