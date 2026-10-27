namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Provides batched/vectorized LINQ operations for <see cref="GpuBigIntArray"/>.
/// </summary>
public static class GpuBigIntVectorLinqExtensions
{
    /// <summary>
    /// Converts a <see cref="GpuBigIntArray"/> into a vectorized deferred expression.
    /// </summary>
    /// <param name="source">The source <see cref="GpuBigIntArray"/>.</param>
    /// <returns>A vectorized expression wrapping the array.</returns>
    public static IGpuBigIntVectorExpression AsVectorizedEnumerable(this GpuBigIntArray source) => new GpuBigIntVectorLeafExpression(source);

    /// <summary>
    /// Filters a vectorized sequence of values based on a predicate.
    /// Executes a Stream Compaction (Where) operation on the GPU.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="predicate">A vectorized boolean expression to test each element.</param>
    /// <returns>A new vectorized expression representing the filtered array.</returns>
    public static IGpuBigIntVectorExpression Where(
        this IGpuBigIntVectorExpression source,
        IGpuBoolVectorExpression predicate) =>
        new VectorWhereExpression(source, predicate);

    /// <summary>
    /// Multiplies each element of a vectorized expression by a scalar.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to multiply each element by.</param>
    /// <returns>A new vectorized expression representing the scalar multiplication.</returns>
    public static IGpuBigIntVectorExpression SelectMultiply(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar) =>
        new VectorMulScalarExpression(source, new GpuBigIntLeafExpression(scalar));

    /// <summary>
    /// Adds a scalar to each element of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to add to each element.</param>
    /// <returns>A new vectorized expression representing the scalar addition.</returns>
    public static IGpuBigIntVectorExpression SelectAdd(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar) =>
        new VectorAddScalarExpression(source, new GpuBigIntLeafExpression(scalar));

    /// <summary>
    /// Subtracts a scalar from each element of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to subtract from each element.</param>
    /// <returns>A new vectorized expression representing the scalar subtraction.</returns>
    public static IGpuBigIntVectorExpression SelectSubtract(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar) =>
        new VectorSubScalarExpression(source, new GpuBigIntLeafExpression(scalar));

    /// <summary>
    /// Divides each element of a vectorized expression by a scalar.
    /// </summary>
    /// <param name="source">The input vectorized expression (dividend).</param>
    /// <param name="scalar">The scalar divisor value.</param>
    /// <returns>A new vectorized expression representing the scalar division.</returns>
    public static IGpuBigIntVectorExpression SelectDivide(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar) =>
        new VectorDivScalarExpression(source, new GpuBigIntLeafExpression(scalar));

    /// <summary>
    /// Computes the remainder (modulo) of each element of a vectorized expression divided by a scalar.
    /// </summary>
    /// <param name="source">The input vectorized expression (dividend).</param>
    /// <param name="scalar">The scalar divisor value.</param>
    /// <returns>A new vectorized expression representing the scalar modulo operation.</returns>
    public static IGpuBigIntVectorExpression SelectModulo(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar) =>
        new VectorModScalarExpression(source, new GpuBigIntLeafExpression(scalar));

    /// <summary>
    /// Computes the floor integer square root (<c>isqrt(x)</c>) of each element in a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression of non-negative big integers.</param>
    /// <returns>A new vectorized expression representing the element-wise integer square root.</returns>
    public static IGpuBigIntVectorExpression SelectSqrt(
        this IGpuBigIntVectorExpression source) =>
        new VectorSqrtExpression(source);

    /// <summary>
    /// Divides each element of a vectorized expression by a 32-bit unsigned integer scalar.
    /// </summary>
    /// <param name="source">The input vectorized expression (dividend).</param>
    /// <param name="divisor">The 32-bit unsigned integer divisor.</param>
    /// <returns>A new vectorized expression representing the element-wise 32-bit scalar division.</returns>
    public static IGpuBigIntVectorExpression SelectDivideByUInt32(
        this IGpuBigIntVectorExpression source,
        uint divisor) =>
        new VectorDivByUInt32Expression(source, divisor);

    /// <summary>
    /// Adds two vectorized expressions element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise addition.</returns>
    public static IGpuBigIntVectorExpression ZipAdd(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right) =>
        new VectorAddVectorExpression(left, right);

    /// <summary>
    /// Subtracts the second vectorized expression from the first element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise subtraction.</returns>
    public static IGpuBigIntVectorExpression ZipSubtract(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right) =>
        new VectorSubVectorExpression(left, right);

    /// <summary>
    /// Multiplies the first vectorized expression by the second element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise multiplication.</returns>
    public static IGpuBigIntVectorExpression ZipMultiply(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right) =>
        new VectorMulVectorExpression(left, right);

    /// <summary>
    /// Divides the first vectorized expression by the second element-wise.
    /// </summary>
    /// <param name="left">The dividend vectorized expression.</param>
    /// <param name="right">The divisor vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise division.</returns>
    public static IGpuBigIntVectorExpression ZipDivide(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right) =>
        new VectorDivVectorExpression(left, right);

    /// <summary>
    /// Computes the remainder (modulo) of the first vectorized expression divided by the second element-wise.
    /// </summary>
    /// <param name="left">The dividend vectorized expression.</param>
    /// <param name="right">The divisor vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise modulo operation.</returns>
    public static IGpuBigIntVectorExpression ZipModulo(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right) =>
        new VectorModVectorExpression(left, right);

    /// <summary>
    /// Computes the sum of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel sum reduction.</returns>
    /// <remarks>
    /// The result has the same number of words as the elements
    /// (<see cref="IGpuBigIntVectorExpression.WordsPerElement"/>). The elements are accumulated in one word more, so a
    /// running sum may leave that width as long as the final sum fits. Evaluating into a destination writes the
    /// magnitude of the result into it and returns a value that wraps it with the sign. Without a destination, a new
    /// buffer is allocated that the caller owns. Evaluation reads two words back from the device.
    /// The sum of an empty vector is +0, as in <c>System.Linq</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown on evaluation into a destination that does not have
    /// <see cref="IGpuBigIntVectorExpression.WordsPerElement"/> words; the destination is not touched.
    /// </exception>
    /// <exception cref="OverflowException">
    /// Thrown on evaluation when the final sum does not fit in <see cref="IGpuBigIntVectorExpression.WordsPerElement"/>
    /// words. Unlike the checked sum of <c>System.Linq</c>, a running sum that leaves the width and comes back does
    /// not throw. The contents of a destination are then unspecified (currently the low words of the magnitude).
    /// </exception>
    public static GpuBigIntExpr Sum(this IGpuBigIntVectorExpression source) => new(new VectorSumReductionExpression(source));

    /// <summary>
    /// Computes the maximum value of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel max reduction.</returns>
    /// <remarks>
    /// The result has the same number of words as the elements; the maximum always fits. Evaluating into a
    /// destination writes the magnitude of the result into it and returns a value that wraps it with the sign.
    /// Without a destination, a new buffer is allocated that the caller owns. Evaluation reads two words back from
    /// the device.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown on evaluation into a destination that does not have
    /// <see cref="IGpuBigIntVectorExpression.WordsPerElement"/> words; the destination is not touched.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown on evaluation when the vector is empty, as in <c>System.Linq</c>. The vector is not evaluated, nothing
    /// is launched, and a destination is not touched.
    /// </exception>
    public static GpuBigIntExpr Max(this IGpuBigIntVectorExpression source) => new(new VectorMaxReductionExpression(source));

    /// <summary>
    /// Computes the minimum value of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel min reduction.</returns>
    /// <remarks>
    /// The result has the same number of words as the elements; the minimum always fits. Evaluating into a
    /// destination writes the magnitude of the result into it and returns a value that wraps it with the sign.
    /// Without a destination, a new buffer is allocated that the caller owns. Evaluation reads two words back from
    /// the device.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown on evaluation into a destination that does not have
    /// <see cref="IGpuBigIntVectorExpression.WordsPerElement"/> words; the destination is not touched.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown on evaluation when the vector is empty, as in <c>System.Linq</c>. The vector is not evaluated, nothing
    /// is launched, and a destination is not touched.
    /// </exception>
    public static GpuBigIntExpr Min(this IGpuBigIntVectorExpression source) => new(new VectorMinReductionExpression(source));

    /// <summary>
    /// Computes the number of elements in the vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the element count; 0 for an empty vector.</returns>
    public static GpuBigIntExpr Count(this IGpuBigIntVectorExpression source) => new(new VectorCountExpression(source));

    /// <summary>
    /// Bypasses a specified number of elements in a sequence and then returns the remaining elements.
    /// </summary>
    /// <param name="source">An <see cref="IGpuBigIntVectorExpression"/> to return elements from.</param>
    /// <param name="count">The number of elements to skip before returning the remaining elements.</param>
    /// <returns>An <see cref="IGpuBigIntVectorExpression"/> that contains the elements that occur after the specified index in the input sequence.</returns>
    public static IGpuBigIntVectorExpression Skip(this IGpuBigIntVectorExpression source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count <= 0)
        {
            return source;
        }

        return new VectorSkipExpression(source, count);
    }

    /// <summary>
    /// Returns a specified number of contiguous elements from the start of a sequence.
    /// </summary>
    /// <param name="source">An <see cref="IGpuBigIntVectorExpression"/> to return elements from.</param>
    /// <param name="count">The number of elements to return.</param>
    /// <returns>An <see cref="IGpuBigIntVectorExpression"/> that contains the specified number of elements from the start of the input sequence.</returns>
    public static IGpuBigIntVectorExpression Take(this IGpuBigIntVectorExpression source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        count = Math.Max(0, count);
        return new VectorTakeExpression(source, count);
    }


    /// <summary>
    /// Evaluates if each element is equal to a scalar.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The scalar is read with its own word count, which may differ from the elements'.
    /// </remarks>
    /// <param name="source">The source vector expression.</param>
    /// <param name="scalar">The scalar value to compare against.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.Equal);

    /// <summary>
    /// Evaluates if each element is not equal to a scalar.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The scalar is read with its own word count, which may differ from the elements'.
    /// </remarks>
    /// <param name="source">The source vector expression.</param>
    /// <param name="scalar">The scalar value to compare against.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.NotEqual);

    /// <summary>
    /// Evaluates if each element is greater than a scalar.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The scalar is read with its own word count, which may differ from the elements'.
    /// </remarks>
    /// <param name="source">The source vector expression.</param>
    /// <param name="scalar">The scalar value to compare against.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.GreaterThan);

    /// <summary>
    /// Evaluates if each element is greater than or equal to a scalar.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The scalar is read with its own word count, which may differ from the elements'.
    /// </remarks>
    /// <param name="source">The source vector expression.</param>
    /// <param name="scalar">The scalar value to compare against.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.GreaterThanOrEqual);

    /// <summary>
    /// Evaluates if each element is less than a scalar.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The scalar is read with its own word count, which may differ from the elements'.
    /// </remarks>
    /// <param name="source">The source vector expression.</param>
    /// <param name="scalar">The scalar value to compare against.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.LessThan);

    /// <summary>
    /// Evaluates if each element is less than or equal to a scalar.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The scalar is read with its own word count, which may differ from the elements'.
    /// </remarks>
    /// <param name="source">The source vector expression.</param>
    /// <param name="scalar">The scalar value to compare against.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.LessThanOrEqual);

    /// <summary>
    /// Evaluates if elements in the left vector are equal to the right vector element-wise.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The two vectors may have different word counts: the missing upper words of the
    /// narrower side are 0. They must have the same length.
    /// </remarks>
    /// <param name="left">The left vector expression.</param>
    /// <param name="right">The right vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.Equal);

    /// <summary>
    /// Evaluates if elements in the left vector are not equal to the right vector element-wise.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The two vectors may have different word counts: the missing upper words of the
    /// narrower side are 0. They must have the same length.
    /// </remarks>
    /// <param name="left">The left vector expression.</param>
    /// <param name="right">The right vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.NotEqual);

    /// <summary>
    /// Evaluates if elements in the left vector are greater than the right vector element-wise.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The two vectors may have different word counts: the missing upper words of the
    /// narrower side are 0. They must have the same length.
    /// </remarks>
    /// <param name="left">The left vector expression.</param>
    /// <param name="right">The right vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.GreaterThan);

    /// <summary>
    /// Evaluates if elements in the left vector are greater than or equal to the right vector element-wise.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The two vectors may have different word counts: the missing upper words of the
    /// narrower side are 0. They must have the same length.
    /// </remarks>
    /// <param name="left">The left vector expression.</param>
    /// <param name="right">The right vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.GreaterThanOrEqual);

    /// <summary>
    /// Evaluates if elements in the left vector are less than the right vector element-wise.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The two vectors may have different word counts: the missing upper words of the
    /// narrower side are 0. They must have the same length.
    /// </remarks>
    /// <param name="left">The left vector expression.</param>
    /// <param name="right">The right vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.LessThan);

    /// <summary>
    /// Evaluates if elements in the left vector are less than or equal to the right vector element-wise.
    /// </summary>
    /// <remarks>
    /// The comparison is signed, as <see cref="System.Numerics.BigInteger.CompareTo(System.Numerics.BigInteger)"/> is; a
    /// negative zero equals zero. The two vectors may have different word counts: the missing upper words of the
    /// narrower side are 0. They must have the same length.
    /// </remarks>
    /// <param name="left">The left vector expression.</param>
    /// <param name="right">The right vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.LessThanOrEqual);

    /// <summary>
    /// Performs an element-wise logical AND on two boolean vector expressions.
    /// </summary>
    /// <param name="left">The left boolean vector expression.</param>
    /// <param name="right">The right boolean vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression And(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right)
        => new VectorLogicalExpression(left, right, isAnd: true);

    /// <summary>
    /// Performs an element-wise logical OR on two boolean vector expressions.
    /// </summary>
    /// <param name="left">The left boolean vector expression.</param>
    /// <param name="right">The right boolean vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression Or(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right)
        => new VectorLogicalExpression(left, right, isAnd: false);

    /// <summary>
    /// Performs an element-wise logical NOT on a boolean vector expression.
    /// </summary>
    /// <param name="source">The source boolean vector expression.</param>
    /// <returns>A boolean vector expression.</returns>
    public static IGpuBoolVectorExpression Not(this IGpuBoolVectorExpression source)
        => new VectorNotExpression(source);

    /// <summary>
    /// Determines whether any element of a boolean vector expression evaluates to true.
    /// </summary>
    /// <param name="source">The source boolean vector expression.</param>
    /// <returns>A boolean scalar expression.</returns>
    public static IGpuBoolExpr Any(this IGpuBoolVectorExpression source)
        => new VectorAnyTrueExpression(source);

    /// <summary>
    /// Determines whether all elements of a boolean vector expression evaluate to true.
    /// </summary>
    /// <param name="source">The source boolean vector expression.</param>
    /// <returns>A boolean scalar expression.</returns>
    public static IGpuBoolExpr All(this IGpuBoolVectorExpression source)
        => new VectorAllTrueExpression(source);
}
