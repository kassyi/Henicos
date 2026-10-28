namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Provides batched/vectorized LINQ operations for <see cref="GpuBigFloatArray"/>.
/// </summary>
public static class GpuBigFloatVectorLinqExtensions
{
    /// <param name="source">The input vectorized expression.</param>
    extension(IGpuBigFloatVectorExpression source)
    {
        /// <summary>
        /// Adds a scalar <see cref="IGpuBigNumber{TSelf}"/> value to each element of a vectorized expression.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to add to each element.</param>
        /// <returns>A new vectorized expression representing the scalar addition.</returns>
        public IGpuBigFloatVectorExpression SelectAdd<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar> =>
            new FloatVectorAddScalarExpression(source, GpuFloatOperand.From(scalar));

        /// <summary>
        /// Subtracts a scalar <see cref="IGpuBigNumber{TSelf}"/> value from each element of a vectorized expression.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to subtract from each element.</param>
        /// <returns>A new vectorized expression representing the scalar subtraction.</returns>
        public IGpuBigFloatVectorExpression SelectSubtract<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar> =>
            new FloatVectorSubScalarExpression(source, GpuFloatOperand.From(scalar));

        /// <summary>
        /// Multiplies each element of a vectorized expression by a scalar <see cref="IGpuBigNumber{TSelf}"/> value.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to multiply each element by.</param>
        /// <returns>A new vectorized expression representing the scalar multiplication.</returns>
        public IGpuBigFloatVectorExpression SelectMultiply<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar> =>
            new FloatVectorMulScalarExpression(source, GpuFloatOperand.From(scalar));

        /// <summary>
        /// Divides each element of a vectorized expression by a scalar <see cref="IGpuBigNumber{TSelf}"/> value.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar divisor value.</param>
        /// <returns>A new vectorized expression representing the scalar division.</returns>
        public IGpuBigFloatVectorExpression SelectDivide<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar> =>
            new FloatVectorDivScalarExpression(source, GpuFloatOperand.From(scalar));

        /// <summary>
        /// Computes the square root (<c>sqrt(x)</c>) of each element in a vectorized expression.
        /// </summary>
        /// <returns>A new vectorized expression representing the element-wise square root.</returns>
        public IGpuBigFloatVectorExpression SelectSqrt() =>
            new FloatVectorUnaryExpression(source, FloatVectorUnaryOpKind.Sqrt);

        /// <summary>
        /// Computes the reciprocal square root (<c>1 / sqrt(x)</c>) of each element in a vectorized expression.
        /// </summary>
        /// <returns>A new vectorized expression representing the element-wise reciprocal square root.</returns>
        public IGpuBigFloatVectorExpression SelectRsqrt() =>
            new FloatVectorUnaryExpression(source, FloatVectorUnaryOpKind.Rsqrt);

        /// <summary>
        /// Computes the reciprocal (<c>1 / x</c>) of each element in a vectorized expression.
        /// </summary>
        /// <returns>A new vectorized expression representing the element-wise reciprocal.</returns>
        public IGpuBigFloatVectorExpression SelectReciprocal() =>
            new FloatVectorUnaryExpression(source, FloatVectorUnaryOpKind.Reciprocal);

        /// <summary>
        /// Divides each element in a vectorized expression by a 32-bit unsigned integer scalar.
        /// </summary>
        /// <param name="divisor">The 32-bit unsigned integer divisor.</param>
        /// <returns>A new vectorized expression representing the element-wise scalar division.</returns>
        public IGpuBigFloatVectorExpression SelectDivideByUInt32(uint divisor) =>
            new FloatVectorDivByUInt32Expression(source, divisor);

        /// <summary>
        /// Computes the natural exponential (<c>e^x</c>) of each element in a vectorized expression.
        /// </summary>
        /// <returns>A new vectorized expression representing the element-wise natural exponential.</returns>
        /// <remarks>
        /// Each element of the result is <see cref="IGpuBigFloatArithmetic.Exp"/> of its source element: the exact value
        /// rounded toward zero to the precision of the element (32 bits per word). The elements are computed one after
        /// another when the expression is evaluated; the first element, in index order, whose result exponent leaves
        /// <see cref="int"/> throws its <see cref="OverflowException"/> from the evaluation.
        /// </remarks>
        public IGpuBigFloatVectorExpression SelectExp() =>
            new FloatVectorUnaryExpression(source, FloatVectorUnaryOpKind.Exp);

        /// <summary>
        /// Computes the natural logarithm (<c>ln(x)</c>) of each element in a vectorized expression.
        /// </summary>
        /// <returns>A new vectorized expression representing the element-wise natural logarithm.</returns>
        /// <remarks>
        /// Each element of the result is <see cref="IGpuBigFloatArithmetic.Log"/> of its source element: the exact value
        /// rounded toward zero to the precision of the element. The elements are computed one after another when the
        /// expression is evaluated; the first element, in index order, that is zero or negative throws its
        /// <see cref="InvalidOperationException"/> from the evaluation.
        /// </remarks>
        public IGpuBigFloatVectorExpression SelectLog() =>
            new FloatVectorUnaryExpression(source, FloatVectorUnaryOpKind.Log);

        /// <summary>
        /// Raises each element in a vectorized expression to the specified scalar power (<c>x^exponent</c>).
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="exponent">The scalar exponent value.</param>
        /// <returns>A new vectorized expression representing the element-wise power operation.</returns>
        /// <remarks>
        /// Each element of the result is <see cref="IGpuBigFloatArithmetic.Pow"/> of its source element and the
        /// exponent: the exact power rounded toward zero to the precision of the result elements (the larger word count
        /// of the vector and the exponent). The elements are computed one after another when the expression is
        /// evaluated; the first element, in index order, that is zero or negative throws its
        /// <see cref="InvalidOperationException"/>, and one whose power leaves the exponent range its
        /// <see cref="OverflowException"/>, from the evaluation.
        /// </remarks>
        public IGpuBigFloatVectorExpression SelectPow<TScalar>(TScalar exponent)
            where TScalar : struct, IGpuBigNumber<TScalar> =>
            new FloatVectorPowScalarExpression(source, GpuFloatOperand.From(exponent));

        /// <summary>
        /// Adds two vectorized floating-point expressions element-wise.
        /// </summary>
        /// <param name="right">The second vectorized expression.</param>
        /// <returns>A new vectorized expression representing the element-wise addition.</returns>
        public IGpuBigFloatVectorExpression ZipAdd(IGpuBigFloatVectorExpression right) =>
            new FloatVectorAddVectorExpression(source, right);

        /// <summary>
        /// Subtracts the second vectorized floating-point expression from the first element-wise.
        /// </summary>
        /// <param name="right">The second vectorized expression.</param>
        /// <returns>A new vectorized expression representing the element-wise subtraction.</returns>
        public IGpuBigFloatVectorExpression ZipSubtract(IGpuBigFloatVectorExpression right) =>
            new FloatVectorSubVectorExpression(source, right);

        /// <summary>
        /// Multiplies the first vectorized floating-point expression by the second element-wise.
        /// </summary>
        /// <param name="right">The second vectorized expression.</param>
        /// <returns>A new vectorized expression representing the element-wise multiplication.</returns>
        public IGpuBigFloatVectorExpression ZipMultiply(IGpuBigFloatVectorExpression right) =>
            new FloatVectorMulVectorExpression(source, right);

        /// <summary>
        /// Divides the first vectorized floating-point expression by the second element-wise.
        /// </summary>
        /// <param name="right">The divisor vectorized expression.</param>
        /// <returns>A new vectorized expression representing the element-wise division.</returns>
        public IGpuBigFloatVectorExpression ZipDivide(IGpuBigFloatVectorExpression right) =>
            new FloatVectorDivVectorExpression(source, right);

        /// <summary>
        /// Raises each element of the first vectorized floating-point expression to the power of the corresponding element in the second expression.
        /// </summary>
        /// <param name="right">The exponent vectorized expression.</param>
        /// <returns>A new vectorized expression representing the element-wise power operation.</returns>
        /// <remarks>
        /// Each element of the result is <see cref="IGpuBigFloatArithmetic.Pow"/> of the corresponding elements: the
        /// exact power rounded toward zero to the precision of the result elements (the larger word count of the two
        /// vectors). The elements are computed one after another when the expression is evaluated; the first element,
        /// in index order, whose base is zero or negative throws its <see cref="InvalidOperationException"/>, and one
        /// whose power leaves the exponent range its <see cref="OverflowException"/>, from the evaluation.
        /// </remarks>
        public IGpuBigFloatVectorExpression ZipPow(IGpuBigFloatVectorExpression right) =>
            new FloatVectorPowVectorExpression(source, right);

        /// <summary>
        /// Bypasses a specified number of elements in a vectorized sequence and then returns the remaining elements.
        /// </summary>
        /// <param name="count">The number of elements to skip before returning the remaining elements.</param>
        /// <returns>An <see cref="IGpuBigFloatVectorExpression"/> that contains the elements that occur after the specified index.</returns>
        public IGpuBigFloatVectorExpression Skip(int count)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (count <= 0)
            {
                return source;
            }

            return new FloatVectorSkipExpression(source, count);
        }

        /// <summary>
        /// Returns a specified number of contiguous elements from the start of a vectorized sequence.
        /// </summary>
        /// <param name="count">The number of elements to return.</param>
        /// <returns>An <see cref="IGpuBigFloatVectorExpression"/> that contains the specified number of elements from the start of the input sequence.</returns>
        public IGpuBigFloatVectorExpression Take(int count)
        {
            ArgumentNullException.ThrowIfNull(source);
            count = Math.Max(0, count);
            return new FloatVectorTakeExpression(source, count);
        }

        /// <summary>
        /// Gets the number of elements in the vectorized expression.
        /// </summary>
        /// <returns>The element count.</returns>
        /// <exception cref="InvalidOperationException">The number is known only on the device (the expression contains a
        /// <c>Where</c>); use <see cref="Count(IGpuBigFloatVectorExpression, GpuWorkspace)"/>.</exception>
        public int Count()
        {
            ArgumentNullException.ThrowIfNull(source);
            return source.Length;
        }

        /// <summary>
        /// Gets the number of elements in the vectorized expression; after a <c>Where</c>, evaluates the expression and
        /// reads the number from the device (one synchronization).
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>The element count.</returns>
        public int Count(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);
            return source.TryGetLength(out var length)
                ? length
                : DeviceCounted.Evaluate(source, workspace).Elements(workspace);
        }

        /// <summary>
        /// Filters a vectorized sequence of floating-point values based on a predicate.
        /// Executes a Stream Compaction (Where) operation on the GPU.
        /// </summary>
        /// <param name="predicate">A vectorized boolean expression to test each element.</param>
        /// <returns>A new vectorized expression representing the filtered array.</returns>
        public IGpuBigFloatVectorExpression Where(IGpuBoolVectorExpression predicate)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            return new FloatVectorWhereExpression(source, predicate);
        }

        /// <summary>
        /// Evaluates if each element is equal to a scalar.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsEqualTo<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar>
            => new FloatVectorCompareScalarExpression(source, GpuFloatOperand.From(scalar), VectorCompareOperator.Equal);

        /// <summary>
        /// Evaluates if each element is not equal to a scalar.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsNotEqualTo<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar>
            => new FloatVectorCompareScalarExpression(source, GpuFloatOperand.From(scalar), VectorCompareOperator.NotEqual);

        /// <summary>
        /// Evaluates if each element is greater than a scalar.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsGreaterThan<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar>
            => new FloatVectorCompareScalarExpression(source, GpuFloatOperand.From(scalar), VectorCompareOperator.GreaterThan);

        /// <summary>
        /// Evaluates if each element is greater than or equal to a scalar.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsGreaterThanOrEqualTo<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar>
            => new FloatVectorCompareScalarExpression(source, GpuFloatOperand.From(scalar), VectorCompareOperator.GreaterThanOrEqual);

        /// <summary>
        /// Evaluates if each element is less than a scalar.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsLessThan<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar>
            => new FloatVectorCompareScalarExpression(source, GpuFloatOperand.From(scalar), VectorCompareOperator.LessThan);

        /// <summary>
        /// Evaluates if each element is less than or equal to a scalar.
        /// </summary>
        /// <typeparam name="TScalar">The scalar GPU big number type.</typeparam>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsLessThanOrEqualTo<TScalar>(TScalar scalar)
            where TScalar : struct, IGpuBigNumber<TScalar>
            => new FloatVectorCompareScalarExpression(source, GpuFloatOperand.From(scalar), VectorCompareOperator.LessThanOrEqual);

        /// <summary>
        /// Evaluates if each element is equal to the corresponding element of another vector.
        /// </summary>
        /// <param name="right">The right vector expression.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsEqualTo(IGpuBigFloatVectorExpression right)
            => new FloatVectorCompareVectorExpression(source, right, VectorCompareOperator.Equal);

        /// <summary>
        /// Evaluates if each element is not equal to the corresponding element of another vector.
        /// </summary>
        /// <param name="right">The right vector expression.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsNotEqualTo(IGpuBigFloatVectorExpression right)
            => new FloatVectorCompareVectorExpression(source, right, VectorCompareOperator.NotEqual);

        /// <summary>
        /// Evaluates if each element is greater than the corresponding element of another vector.
        /// </summary>
        /// <param name="right">The right vector expression.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsGreaterThan(IGpuBigFloatVectorExpression right)
            => new FloatVectorCompareVectorExpression(source, right, VectorCompareOperator.GreaterThan);

        /// <summary>
        /// Evaluates if each element is greater than or equal to the corresponding element of another vector.
        /// </summary>
        /// <param name="right">The right vector expression.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsGreaterThanOrEqualTo(IGpuBigFloatVectorExpression right)
            => new FloatVectorCompareVectorExpression(source, right, VectorCompareOperator.GreaterThanOrEqual);

        /// <summary>
        /// Evaluates if each element is less than the corresponding element of another vector.
        /// </summary>
        /// <param name="right">The right vector expression.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsLessThan(IGpuBigFloatVectorExpression right)
            => new FloatVectorCompareVectorExpression(source, right, VectorCompareOperator.LessThan);

        /// <summary>
        /// Evaluates if each element is less than or equal to the corresponding element of another vector.
        /// </summary>
        /// <param name="right">The right vector expression.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsLessThanOrEqualTo(IGpuBigFloatVectorExpression right)
            => new FloatVectorCompareVectorExpression(source, right, VectorCompareOperator.LessThanOrEqual);

        /// <summary>
        /// Computes the sum of all elements in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>
        /// A <see cref="GpuBigFloat"/> containing the sum of all elements; +0 for an empty sequence, as
        /// <c>System.Linq</c> returns.
        /// </returns>
        public GpuBigFloat Sum(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            return FloatVectorReductionOrchestrator.Reduce(workspace, source, FloatReductionKind.Sum);
        }

        /// <summary>
        /// Computes the maximum value in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>A <see cref="GpuBigFloat"/> containing the maximum value.</returns>
        /// <exception cref="InvalidOperationException">The sequence is empty (as in <c>System.Linq</c>).</exception>
        public GpuBigFloat Max(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            return FloatVectorReductionOrchestrator.Reduce(workspace, source, FloatReductionKind.Max);
        }

        /// <summary>
        /// Computes the minimum value in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>A <see cref="GpuBigFloat"/> containing the minimum value.</returns>
        /// <exception cref="InvalidOperationException">The sequence is empty (as in <c>System.Linq</c>).</exception>
        public GpuBigFloat Min(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            return FloatVectorReductionOrchestrator.Reduce(workspace, source, FloatReductionKind.Min);
        }

        /// <summary>
        /// Computes the average of all elements in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>A <see cref="GpuBigFloat"/> containing the average value.</returns>
        /// <exception cref="InvalidOperationException">The sequence is empty (as in <c>System.Linq</c>).</exception>
        public GpuBigFloat Average(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            // One evaluation gives the sum and the number of elements (after a Where, known only on the device). The
            // sum of nothing is 0; stop before dividing it by a count of 0.
            using var sum = FloatVectorReductionOrchestrator.ReduceWithCount(workspace, source, FloatReductionKind.Sum,
                out var count);
            if (count == 0)
            {
                throw new InvalidOperationException("Sequence contains no elements");
            }

            var countWords = new uint[source.WordsPerElement];
            countWords[0] = (uint)count;
            using var countSig = new GpuBigUInt(workspace, countWords);
            var countFloat = new GpuBigFloat(countSig, 0, false);

            return sum / countFloat;
        }

        /// <summary>
        /// Evaluates the vectorized floating-point expression directly into an existing destination <see cref="GpuBigFloatArray"/>.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <param name="destination">The pre-allocated destination array.</param>
        /// <exception cref="InvalidOperationException">The expression contains a <c>Where</c>, whose length is known only
        /// on the device; use <c>Evaluate</c>. The destination is not written.</exception>
        public void EvaluateTo(GpuWorkspace workspace,
            GpuBigFloatArray destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            // EvaluateTo returns no count, so a vector whose length is known only on the device (after Where) has no
            // place to say how much of the destination it wrote (design D3 4.1.3)
            if (!source.TryGetLength(out var length))
            {
                throw new InvalidOperationException(
                    "The length of a filtered vector is known only on the device. Use Evaluate, which returns exactly " +
                    "the selected elements, or Count(workspace).");
            }

            if (destination.Length < length)
            {
                throw new ArgumentException("Destination array is smaller than the source expression length.",
                    nameof(destination));
            }

            source.Evaluate(workspace, 0, length, destination);
        }
    }

    /// <summary>
    /// Converts a <see cref="GpuBigFloatArray"/> into a vectorized deferred expression.
    /// </summary>
    /// <param name="source">The source <see cref="GpuBigFloatArray"/>.</param>
    /// <returns>A vectorized expression wrapping the array.</returns>
    public static IGpuBigFloatVectorExpression AsVectorizedEnumerable(this GpuBigFloatArray source) =>
        new GpuBigFloatVectorLeafExpression(source);
}
