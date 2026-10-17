namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides extension methods for zero-allocation stack-based LINQ operations on <see cref="IGpuBigNumber{TSelf}"/> sequences.
/// </summary>
public static class GpuBigNumberStackLinqExtensions
{
    /// <param name="source">The sequence of elements to operate on.</param>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    extension<TEnumerator, T>(ParallelStackEnumerable<TEnumerator, T> source)
        where TEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T>
    {
        /// <summary>
        /// Projects each element of a parallel sequence of <typeparamref name="T"/> values into a new form.
        /// </summary>
        /// <param name="selector">A transform function to apply to each source element.</param>
        /// <returns>A new parallel sequence containing the transformed elements.</returns>
        public ParallelStackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            Select(Func<T, T> selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            return new ParallelStackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>(
                new SelectEnumerator<TEnumerator, T, T>(source.Enumerator, selector));
        }

        /// <summary>
        /// Filters a parallel sequence of <typeparamref name="T"/> values based on a predicate.
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <returns>A new parallel sequence that contains elements from the input sequence that satisfy the condition.</returns>
        public ParallelStackEnumerable<WhereEnumerator<TEnumerator, T>, T>
            Where(Func<T, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return new ParallelStackEnumerable<WhereEnumerator<TEnumerator, T>, T>(
                new WhereEnumerator<TEnumerator, T>(source.Enumerator, predicate));
        }

        /// <summary>
        /// Adds a scalar <typeparamref name="T"/> to each element of a parallel sequence.
        /// </summary>
        /// <param name="scalar">The scalar value to add.</param>
        /// <returns>A new parallel sequence containing the sums.</returns>
        public ParallelStackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectAdd(T scalar) => source.Select(x => x + scalar);

        /// <summary>
        /// Subtracts a scalar <typeparamref name="T"/> from each element of a parallel sequence.
        /// </summary>
        /// <param name="scalar">The scalar subtrahend.</param>
        /// <returns>A new parallel sequence containing the differences.</returns>
        public ParallelStackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectSubtract(T scalar) => source.Select(x => x - scalar);

        /// <summary>
        /// Multiplies each element of a parallel sequence by a scalar <typeparamref name="T"/>.
        /// </summary>
        /// <param name="scalar">The scalar multiplier.</param>
        /// <returns>A new parallel sequence containing the products.</returns>
        public ParallelStackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectMultiply(T scalar) => source.Select(x => x * scalar);

        /// <summary>
        /// Divides each element of a parallel sequence by a scalar <typeparamref name="T"/>.
        /// </summary>
        /// <param name="scalar">The scalar divisor.</param>
        /// <returns>A new parallel sequence containing the quotients.</returns>
        public ParallelStackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectDivide(T scalar) => source.Select(x => x / scalar);

        /// <summary>
        /// Computes the sum of a parallel sequence of <typeparamref name="T"/> values by constructing a balanced binary evaluation tree.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the sum.</returns>
        public T Sum() =>
            StackLinqReductionCore.ReduceBalancedTree<TEnumerator, T, T>(
                source.Enumerator,
                static x => Clone(x),
                static (a, b) =>
                {
                    var sum = a + b;
                    a.Dispose();
                    b.Dispose();
                    return sum;
                });

        /// <summary>
        /// Computes the maximum of a parallel sequence of <typeparamref name="T"/> values by constructing a balanced binary evaluation tree.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the maximum value.</returns>
        public T Max() =>
            StackLinqReductionCore.ReduceBalancedTree<TEnumerator, T, T>(
                source.Enumerator,
                static x => Clone(x),
                static (a, b) =>
                {
                    if (a.CompareTo(b) >= 0)
                    {
                        b.Dispose();
                        return a;
                    }

                    a.Dispose();
                    return b;
                });

        /// <summary>
        /// Computes the minimum of a parallel sequence of <typeparamref name="T"/> values by constructing a balanced binary evaluation tree.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the minimum value.</returns>
        public T Min() =>
            StackLinqReductionCore.ReduceBalancedTree<TEnumerator, T, T>(
                source.Enumerator,
                static x => Clone(x),
                static (a, b) =>
                {
                    if (a.CompareTo(b) <= 0)
                    {
                        b.Dispose();
                        return a;
                    }

                    a.Dispose();
                    return b;
                });

        /// <summary>
        /// Applies an accumulator function over a parallel sequence of <typeparamref name="T"/> values by constructing a balanced binary evaluation tree.
        /// </summary>
        /// <param name="func">An accumulator function to be invoked on each pair of values.</param>
        /// <returns>The aggregated <typeparamref name="T"/> value.</returns>
        public T Aggregate(Func<T, T, T> func)
        {
            ArgumentNullException.ThrowIfNull(func);
            return StackLinqReductionCore.ReduceBalancedTree<TEnumerator, T, T>(
                source.Enumerator,
                static x => Clone(x),
                (a, b) =>
                {
                    var res = func(a, b);
                    if (a.Magnitude.DevicePointer != res.Magnitude.DevicePointer)
                    {
                        a.Dispose();
                    }

                    if (b.Magnitude.DevicePointer != res.Magnitude.DevicePointer)
                    {
                        b.Dispose();
                    }

                    return res;
                });
        }

        /// <summary>
        /// Computes the average of a parallel sequence of <typeparamref name="T"/> values by constructing a balanced binary evaluation tree.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the average.</returns>
        public T Average()
        {
            using var sum = StackLinqReductionCore.ReduceBalancedTree<TEnumerator, T, T>(
                source.Enumerator,
                static x => Clone(x),
                static (a, b) =>
                {
                    var res = a + b;
                    a.Dispose();
                    b.Dispose();
                    return res;
                },
                out var count);

            if (count == 1)
            {
                return Clone(sum);
            }

            using var countVal = CreateFromUInt32<T>(sum.Workspace, sum.Magnitude.NumWords, (uint)count);
            return sum / countVal;
        }

        /// <summary>
        /// Computes the number of elements in a parallel sequence of <typeparamref name="T"/> values.
        /// </summary>
        /// <returns>The number of elements in the sequence.</returns>
        public int Count() =>
            StackLinqReductionCore.CountElements<TEnumerator, T>(source.Enumerator);
    }

    /// <param name="source">The sequence of elements to operate on.</param>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    extension<TEnumerator, T>(StackEnumerable<TEnumerator, T> source)
        where TEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T>
    {
        /// <summary>
        /// Adds a scalar <typeparamref name="T"/> to each element of a stack-allocated sequence.
        /// </summary>
        /// <param name="scalar">The scalar value to add.</param>
        /// <returns>A new stack-allocated sequence containing the sums.</returns>
        public StackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectAdd(T scalar) =>
            source.Select(x => x + scalar);

        /// <summary>
        /// Subtracts a scalar <typeparamref name="T"/> from each element of a stack-allocated sequence.
        /// </summary>
        /// <param name="scalar">The scalar subtrahend.</param>
        /// <returns>A new stack-allocated sequence containing the differences.</returns>
        public StackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectSubtract(T scalar) =>
            source.Select(x => x - scalar);

        /// <summary>
        /// Multiplies each element of a stack-allocated sequence by a scalar <typeparamref name="T"/>.
        /// </summary>
        /// <param name="scalar">The scalar multiplier.</param>
        /// <returns>A new stack-allocated sequence containing the products.</returns>
        public StackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectMultiply(T scalar) =>
            source.Select(x => x * scalar);

        /// <summary>
        /// Divides each element of a stack-allocated sequence by a scalar <typeparamref name="T"/>.
        /// </summary>
        /// <param name="scalar">The scalar divisor.</param>
        /// <returns>A new stack-allocated sequence containing the quotients.</returns>
        public StackEnumerable<SelectEnumerator<TEnumerator, T, T>, T>
            SelectDivide(T scalar) =>
            source.Select(x => x / scalar);

        /// <summary>
        /// Computes the sum of a sequence of <typeparamref name="T"/> values.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the sum.</returns>
        public T Sum() =>
            source.AsParallel().Sum();

        /// <summary>
        /// Computes the maximum of a sequence of <typeparamref name="T"/> values.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the maximum value.</returns>
        public T Max() =>
            source.AsParallel().Max();

        /// <summary>
        /// Computes the minimum of a sequence of <typeparamref name="T"/> values.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the minimum value.</returns>
        public T Min() =>
            source.AsParallel().Min();

        /// <summary>
        /// Computes the average of a sequence of <typeparamref name="T"/> values.
        /// </summary>
        /// <returns>A new <typeparamref name="T"/> containing the average.</returns>
        public T Average() =>
            source.AsParallel().Average();
    }

    /// <summary>
    /// Adds two parallel sequences of <typeparamref name="T"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The first parallel sequence.</param>
    /// <param name="right">The second parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise sums.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipAdd<TLeftEnumerator, TRightEnumerator, T>(
            this ParallelStackEnumerable<TLeftEnumerator, T> left,
            ParallelStackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a + b));

    /// <summary>
    /// Subtracts the second parallel sequence of <typeparamref name="T"/> values from the first element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The minuend parallel sequence.</param>
    /// <param name="right">The subtrahend parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise differences.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipSubtract<TLeftEnumerator, TRightEnumerator, T>(
            this ParallelStackEnumerable<TLeftEnumerator, T> left,
            ParallelStackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a - b));

    /// <summary>
    /// Multiplies two parallel sequences of <typeparamref name="T"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The multiplicand parallel sequence.</param>
    /// <param name="right">The multiplier parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise products.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipMultiply<TLeftEnumerator, TRightEnumerator, T>(
            this ParallelStackEnumerable<TLeftEnumerator, T> left,
            ParallelStackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a * b));

    /// <summary>
    /// Divides the first parallel sequence of <typeparamref name="T"/> values by the second element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The dividend parallel sequence.</param>
    /// <param name="right">The divisor parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise quotients.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipDivide<TLeftEnumerator, TRightEnumerator, T>(
            this ParallelStackEnumerable<TLeftEnumerator, T> left,
            ParallelStackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Applies an accumulator function over a sequence of <see cref="GpuBigFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of elements to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The aggregated <see cref="GpuBigFloat"/> value.</returns>
    public static GpuBigFloat Aggregate<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigFloat> source,
        Func<GpuBigFloat, GpuBigFloat, GpuBigFloat> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigFloat>, allows ref struct =>
        source.AsParallel().Aggregate(func);

    /// <summary>
    /// Applies an accumulator function over a sequence of <see cref="GpuBigUFloat"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of elements to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The aggregated <see cref="GpuBigUFloat"/> value.</returns>
    public static GpuBigUFloat Aggregate<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigUFloat> source,
        Func<GpuBigUFloat, GpuBigUFloat, GpuBigUFloat> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigUFloat>, allows ref struct =>
        source.AsParallel().Aggregate(func);

    /// <summary>
    /// Applies an accumulator function over a sequence of <see cref="GpuBigUInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of elements to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each element.</param>
    /// <returns>The aggregated <see cref="GpuBigUInt"/> value.</returns>
    public static GpuBigUInt Aggregate<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigUInt> source,
        Func<GpuBigUInt, GpuBigUInt, GpuBigUInt> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigUInt>, allows ref struct =>
        source.AsParallel().Aggregate(func);

    /// <summary>
    /// Adds two stack-allocated sequences of <typeparamref name="T"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The first sequence.</param>
    /// <param name="right">The second sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise sums.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipAdd<TLeftEnumerator, TRightEnumerator, T>(
            this StackEnumerable<TLeftEnumerator, T> left,
            StackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a + b));

    /// <summary>
    /// Subtracts the second stack-allocated sequence of <typeparamref name="T"/> values from the first element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The minuend sequence.</param>
    /// <param name="right">The subtrahend sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise differences.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipSubtract<TLeftEnumerator, TRightEnumerator, T>(
            this StackEnumerable<TLeftEnumerator, T> left,
            StackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a - b));

    /// <summary>
    /// Multiplies two stack-allocated sequences of <typeparamref name="T"/> values element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The multiplicand sequence.</param>
    /// <param name="right">The multiplier sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise products.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipMultiply<TLeftEnumerator, TRightEnumerator, T>(
            this StackEnumerable<TLeftEnumerator, T> left,
            StackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a * b));

    /// <summary>
    /// Divides the first stack-allocated sequence of <typeparamref name="T"/> values by the second element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="left">The dividend sequence.</param>
    /// <param name="right">The divisor sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise quotients.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>, T>
        ZipDivide<TLeftEnumerator, TRightEnumerator, T>(
            this StackEnumerable<TLeftEnumerator, T> left,
            StackEnumerable<TRightEnumerator, T> right)
        where TLeftEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<T>, allows ref struct
        where T : struct, IGpuBigNumber<T> =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, T, T, T>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    private static T Clone<T>(T source)
        where T : struct, IGpuBigNumber<T>
    {
        var sig = source.Magnitude;
        var workspace = source.Workspace;
        var copySig = new GpuBigUInt(workspace, sig.NumWords);
        workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, sig.DevicePointer, sig.NumWords, workspace.Stream);
        return T.Create(copySig, source.Exponent, source.IsNegative);
    }

    private static T CreateFromUInt32<T>(GpuWorkspace workspace, int numWords, uint value)
        where T : struct, IGpuBigNumber<T>
    {
        var words = ArrayPool<uint>.Shared.Rent(numWords);
        try
        {
            words.AsSpan(0, numWords).Clear();
            words[0] = value;
            var sig = new GpuBigUInt(workspace, numWords);
            workspace.Provider.CopyHostToDevice(sig.DevicePointer, words, numWords, workspace.Stream);
            return T.Create(sig, 0, false);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(words);
        }
    }
}
