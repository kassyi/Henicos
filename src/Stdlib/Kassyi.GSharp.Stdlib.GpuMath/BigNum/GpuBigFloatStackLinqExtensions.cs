using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides extension methods for zero-allocation stack-based LINQ operations on <see cref="GpuBigFloat"/> sequences.
/// </summary>
public static class GpuBigFloatStackLinqExtensions
{
    private static GpuBigFloat Clone(GpuBigFloat source)
    {
        var sig = source.UFloat.Significand;
        var workspace = sig.Workspace;
        var copySig = new GpuBigUInt(workspace, sig.NumWords);
        workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, sig.DevicePointer, sig.NumWords, workspace.Stream);
        return new GpuBigFloat(copySig, source.UFloat.Exponent, source.IsNegative);
    }

    /// <summary>
    /// Projects each element of a parallel sequence of <see cref="GpuBigFloat"/> values into a new form.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each source element.</param>
    /// <returns>A new parallel sequence containing the transformed elements.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        Select<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
            Func<GpuBigFloat, GpuBigFloat> selector)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>(
            new SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>(source.Enumerator, selector));
    }

    /// <summary>
    /// Filters a parallel sequence of <see cref="GpuBigFloat"/> values based on a predicate.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">An enumerable to filter.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A new parallel sequence that contains elements from the input sequence that satisfy the condition.</returns>
    public static ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigFloat>, GpuBigFloat>
        Where<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
            Func<GpuBigFloat, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigFloat>, GpuBigFloat>(
            new WhereEnumerator<TEnumerator, GpuBigFloat>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Adds a scalar <see cref="GpuBigFloat"/> to each element of a parallel sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of operands.</param>
    /// <param name="scalar">The scalar value to add.</param>
    /// <returns>A new parallel sequence containing the sums.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectAdd<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => x + scalar);

    /// <summary>
    /// Subtracts a scalar <see cref="GpuBigFloat"/> from each element of a parallel sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of minuends.</param>
    /// <param name="scalar">The scalar subtrahend.</param>
    /// <returns>A new parallel sequence containing the differences.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectSubtract<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => x - scalar);

    /// <summary>
    /// Multiplies each element of a parallel sequence by a scalar <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of multiplicands.</param>
    /// <param name="scalar">The scalar multiplier.</param>
    /// <returns>A new parallel sequence containing the products.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectMultiply<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => x * scalar);

    /// <summary>
    /// Divides each element of a parallel sequence by a scalar <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of dividends.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new parallel sequence containing the quotients.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectDivide<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => x / scalar);

    /// <summary>
    /// Adds two parallel sequences of <see cref="GpuBigFloat"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The first parallel sequence.</param>
    /// <param name="right">The second parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise sums.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipAdd<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a + b));

    /// <summary>
    /// Subtracts the second parallel sequence of <see cref="GpuBigFloat"/> values from the first element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The minuend parallel sequence.</param>
    /// <param name="right">The subtrahend parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise differences.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipSubtract<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a - b));

    /// <summary>
    /// Multiplies two parallel sequences of <see cref="GpuBigFloat"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The multiplicand parallel sequence.</param>
    /// <param name="right">The multiplier parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise products.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipMultiply<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a * b));

    /// <summary>
    /// Divides the first parallel sequence of <see cref="GpuBigFloat"/> values by the second element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend parallel sequence.</param>
    /// <param name="right">The divisor parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise quotients.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipDivide<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Adds a scalar <see cref="GpuBigFloat"/> to each element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of operands.</param>
    /// <param name="scalar">The scalar value to add.</param>
    /// <returns>A new stack-allocated sequence containing the sums.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectAdd<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => (GpuBigFloat)(x + scalar));

    /// <summary>
    /// Subtracts a scalar <see cref="GpuBigFloat"/> from each element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of minuends.</param>
    /// <param name="scalar">The scalar subtrahend.</param>
    /// <returns>A new stack-allocated sequence containing the differences.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectSubtract<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => (GpuBigFloat)(x - scalar));

    /// <summary>
    /// Multiplies each element of a stack-allocated sequence by a scalar <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of multiplicands.</param>
    /// <param name="scalar">The scalar multiplier.</param>
    /// <returns>A new stack-allocated sequence containing the products.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectMultiply<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => (GpuBigFloat)(x * scalar));

    /// <summary>
    /// Divides each element of a stack-allocated sequence by a scalar <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of dividends.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new stack-allocated sequence containing the quotients.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        SelectDivide<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigFloat> source,
            GpuBigFloat scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.Select(x => (GpuBigFloat)(x / scalar));

    /// <summary>
    /// Adds two stack-allocated sequences of <see cref="GpuBigFloat"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The first sequence.</param>
    /// <param name="right">The second sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise sums.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipAdd<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            StackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a + b));

    /// <summary>
    /// Subtracts the second stack-allocated sequence of <see cref="GpuBigFloat"/> values from the first element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The minuend sequence.</param>
    /// <param name="right">The subtrahend sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise differences.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipSubtract<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            StackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a - b));

    /// <summary>
    /// Multiplies two stack-allocated sequences of <see cref="GpuBigFloat"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The multiplicand sequence.</param>
    /// <param name="right">The multiplier sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise products.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipMultiply<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            StackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a * b));

    /// <summary>
    /// Divides the first stack-allocated sequence of <see cref="GpuBigFloat"/> values by the second element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend sequence.</param>
    /// <param name="right">The divisor sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise quotients.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>, GpuBigFloat>
        ZipDivide<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigFloat> left,
            StackEnumerable<TRightEnumerator, GpuBigFloat> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigFloat, GpuBigFloat, GpuBigFloat>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to sum.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the sum.</returns>
    public static GpuBigFloat Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return Clone(first);
        }

        GpuBigFloat acc = first + second;
        try
        {
            while (enumerator.TryGetNext(out var next))
            {
                GpuBigFloat nextAcc = acc + next;
                acc.Dispose();
                acc = nextAcc;
            }

            return acc;
        }
        catch
        {
            acc.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Computes the maximum of a parallel sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to find the maximum of.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the maximum value.</returns>
    public static GpuBigFloat Max<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var best))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var next))
        {
            if (next > best)
            {
                best = next;
            }
        }

        return Clone(best);
    }

    /// <summary>
    /// Computes the minimum of a parallel sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to find the minimum of.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the minimum value.</returns>
    public static GpuBigFloat Min<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var best))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        while (enumerator.TryGetNext(out var next))
        {
            if (next < best)
            {
                best = next;
            }
        }

        return Clone(best);
    }

    /// <summary>
    /// Applies an accumulator function over a parallel sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The aggregated <see cref="GpuBigFloat"/> value.</returns>
    public static GpuBigFloat Aggregate<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source,
        Func<GpuBigFloat, GpuBigFloat, GpuBigFloat> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(func);

        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return Clone(first);
        }

        var acc = func(first, second);
        try
        {
            while (enumerator.TryGetNext(out var next))
            {
                var prevAcc = acc;
                acc = func(prevAcc, next);
                if (prevAcc.UFloat.Significand.DevicePointer != acc.UFloat.Significand.DevicePointer &&
                    prevAcc.UFloat.Significand.DevicePointer != first.UFloat.Significand.DevicePointer &&
                    prevAcc.UFloat.Significand.DevicePointer != second.UFloat.Significand.DevicePointer)
                {
                    prevAcc.Dispose();
                }
            }

            return acc;
        }
        catch
        {
            acc.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Computes the average of a parallel sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to calculate the average of.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the average.</returns>
    public static GpuBigFloat Average<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return Clone(first);
        }

        var count = 2;
        GpuBigFloat sum = first + second;
        try
        {
            while (enumerator.TryGetNext(out var next))
            {
                GpuBigFloat nextSum = sum + next;
                sum.Dispose();
                sum = nextSum;
                count++;
            }

            var workspace = first.UFloat.Significand.Workspace;
            var numWords = first.UFloat.Significand.NumWords;
            var countWords = new uint[numWords];
            countWords[0] = (uint)count;
            using var countSig = new GpuBigUInt(workspace, countWords);
            var countFloat = new GpuBigFloat(countSig, 0, false);

            return sum / countFloat;
        }
        finally
        {
            sum.Dispose();
        }
    }

    /// <summary>
    /// Computes the number of elements in a parallel sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence.</param>
    /// <returns>The number of elements in the sequence.</returns>
    public static int Count<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        StackLinqReductionCore.CountElements<TEnumerator, GpuBigFloat>(source.Enumerator);

    /// <summary>
    /// Computes the sum of a sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to sum.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the sum.</returns>
    public static GpuBigFloat Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.AsParallel().Sum();

    /// <summary>
    /// Computes the maximum of a sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to find the maximum of.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the maximum value.</returns>
    public static GpuBigFloat Max<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.AsParallel().Max();

    /// <summary>
    /// Computes the minimum of a sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to find the minimum of.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the minimum value.</returns>
    public static GpuBigFloat Min<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.AsParallel().Min();

    /// <summary>
    /// Applies an accumulator function over a sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The aggregated <see cref="GpuBigFloat"/> value.</returns>
    public static GpuBigFloat Aggregate<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigFloat> source,
        Func<GpuBigFloat, GpuBigFloat, GpuBigFloat> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.AsParallel().Aggregate(func);

    /// <summary>
    /// Computes the average of a sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to calculate the average of.</param>
    /// <returns>A new <see cref="GpuBigFloat"/> containing the average.</returns>
    public static GpuBigFloat Average<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigFloat> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.AsParallel().Average();
}
