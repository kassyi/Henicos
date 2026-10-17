namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Provides batched/vectorized LINQ operations for <see cref="GpuBigFloatArray"/>.
/// </summary>
public static class GpuBigFloatVectorLinqExtensions
{
    /// <summary>
    /// Converts a <see cref="GpuBigFloatArray"/> into a vectorized deferred expression.
    /// </summary>
    /// <param name="source">The source <see cref="GpuBigFloatArray"/>.</param>
    /// <returns>A vectorized expression wrapping the array.</returns>
    public static IGpuBigFloatVectorExpression AsVectorizedEnumerable(this GpuBigFloatArray source) =>
        new GpuBigFloatVectorLeafExpression(source);

    /// <param name="source">The input vectorized expression.</param>
    extension(IGpuBigFloatVectorExpression source)
    {
        /// <summary>
        /// Adds a scalar <see cref="GpuBigFloat"/> to each element of a vectorized expression.
        /// </summary>
        /// <param name="scalar">The scalar value to add to each element.</param>
        /// <returns>A new vectorized expression representing the scalar addition.</returns>
        public IGpuBigFloatVectorExpression SelectAdd(GpuBigFloat scalar) =>
            new FloatVectorAddScalarExpression(source, scalar);

        /// <summary>
        /// Subtracts a scalar <see cref="GpuBigFloat"/> from each element of a vectorized expression.
        /// </summary>
        /// <param name="scalar">The scalar value to subtract from each element.</param>
        /// <returns>A new vectorized expression representing the scalar subtraction.</returns>
        public IGpuBigFloatVectorExpression SelectSubtract(GpuBigFloat scalar) =>
            new FloatVectorSubScalarExpression(source, scalar);

        /// <summary>
        /// Multiplies each element of a vectorized expression by a scalar <see cref="GpuBigFloat"/>.
        /// </summary>
        /// <param name="scalar">The scalar value to multiply each element by.</param>
        /// <returns>A new vectorized expression representing the scalar multiplication.</returns>
        public IGpuBigFloatVectorExpression SelectMultiply(GpuBigFloat scalar) =>
            new FloatVectorMulScalarExpression(source, scalar);

        /// <summary>
        /// Divides each element of a vectorized expression by a scalar <see cref="GpuBigFloat"/>.
        /// </summary>
        /// <param name="scalar">The scalar divisor value.</param>
        /// <returns>A new vectorized expression representing the scalar division.</returns>
        public IGpuBigFloatVectorExpression SelectDivide(GpuBigFloat scalar) =>
            new FloatVectorDivScalarExpression(source, scalar);

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
        public int Count()
        {
            ArgumentNullException.ThrowIfNull(source);
            return source.Length;
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
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsEqualTo(GpuBigFloat scalar)
            => new FloatVectorCompareScalarExpression(source, scalar, VectorCompareOperator.Equal);

        /// <summary>
        /// Evaluates if each element is not equal to a scalar.
        /// </summary>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsNotEqualTo(GpuBigFloat scalar)
            => new FloatVectorCompareScalarExpression(source, scalar, VectorCompareOperator.NotEqual);

        /// <summary>
        /// Evaluates if each element is greater than a scalar.
        /// </summary>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsGreaterThan(GpuBigFloat scalar)
            => new FloatVectorCompareScalarExpression(source, scalar, VectorCompareOperator.GreaterThan);

        /// <summary>
        /// Evaluates if each element is greater than or equal to a scalar.
        /// </summary>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsGreaterThanOrEqualTo(GpuBigFloat scalar)
            => new FloatVectorCompareScalarExpression(source, scalar, VectorCompareOperator.GreaterThanOrEqual);

        /// <summary>
        /// Evaluates if each element is less than a scalar.
        /// </summary>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsLessThan(GpuBigFloat scalar)
            => new FloatVectorCompareScalarExpression(source, scalar, VectorCompareOperator.LessThan);

        /// <summary>
        /// Evaluates if each element is less than or equal to a scalar.
        /// </summary>
        /// <param name="scalar">The scalar value to compare against.</param>
        /// <returns>A boolean vector expression.</returns>
        public IGpuBoolVectorExpression IsLessThanOrEqualTo(GpuBigFloat scalar)
            => new FloatVectorCompareScalarExpression(source, scalar, VectorCompareOperator.LessThanOrEqual);

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
        /// <returns>A <see cref="GpuBigFloat"/> containing the sum of all elements.</returns>
        public GpuBigFloat Sum(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            var evaluated = source.Evaluate(workspace);
            return FloatVectorReductionOrchestrator.Reduce(workspace, evaluated, FloatReductionKind.Sum);
        }

        /// <summary>
        /// Computes the maximum value in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>A <see cref="GpuBigFloat"/> containing the maximum value.</returns>
        public GpuBigFloat Max(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            var evaluated = source.Evaluate(workspace);
            return FloatVectorReductionOrchestrator.Reduce(workspace, evaluated, FloatReductionKind.Max);
        }

        /// <summary>
        /// Computes the minimum value in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>A <see cref="GpuBigFloat"/> containing the minimum value.</returns>
        public GpuBigFloat Min(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            var evaluated = source.Evaluate(workspace);
            return FloatVectorReductionOrchestrator.Reduce(workspace, evaluated, FloatReductionKind.Min);
        }

        /// <summary>
        /// Computes the average of all elements in a vectorized floating-point expression.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <returns>A <see cref="GpuBigFloat"/> containing the average value.</returns>
        public GpuBigFloat Average(GpuWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            using var sum = source.Sum(workspace);
            var countWords = new uint[source.WordsPerElement];
            countWords[0] = (uint)source.Length;
            using var countSig = new GpuBigUInt(workspace, countWords);
            var countFloat = new GpuBigFloat(countSig, 0, false);

            return sum / countFloat;
        }

        /// <summary>
        /// Evaluates the vectorized floating-point expression directly into an existing destination <see cref="GpuBigFloatArray"/>.
        /// </summary>
        /// <param name="workspace">The GPU workspace used for evaluation.</param>
        /// <param name="destination">The pre-allocated destination array.</param>
        public void EvaluateTo(GpuWorkspace workspace,
            GpuBigFloatArray destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);

            if (destination.Length < source.Length)
            {
                throw new ArgumentException("Destination array is smaller than the source expression length.",
                    nameof(destination));
            }

            source.Evaluate(workspace, 0, source.Length, destination);
        }
    }
}
