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
    public static IGpuBigIntVectorExpression AsVectorizedEnumerable(this GpuBigIntArray source)
    {
        return new GpuBigIntVectorLeafExpression(source);
    }

    /// <summary>
    /// Multiplies each element of a vectorized expression by a scalar.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to multiply each element by.</param>
    /// <returns>A new vectorized expression representing the scalar multiplication.</returns>
    public static IGpuBigIntVectorExpression SelectMultiply(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar)
    {
        return new VectorMulScalarExpression(source, new GpuBigIntLeafExpression(scalar));
    }

    /// <summary>
    /// Adds a scalar to each element of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to add to each element.</param>
    /// <returns>A new vectorized expression representing the scalar addition.</returns>
    public static IGpuBigIntVectorExpression SelectAdd(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar)
    {
        return new VectorAddScalarExpression(source, new GpuBigIntLeafExpression(scalar));
    }

    /// <summary>
    /// Subtracts a scalar from each element of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to subtract from each element.</param>
    /// <returns>A new vectorized expression representing the scalar subtraction.</returns>
    public static IGpuBigIntVectorExpression SelectSubtract(
        this IGpuBigIntVectorExpression source,
        GpuBigInt scalar)
    {
        return new VectorSubScalarExpression(source, new GpuBigIntLeafExpression(scalar));
    }

    /// <summary>
    /// Adds two vectorized expressions element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise addition.</returns>
    public static IGpuBigIntVectorExpression ZipAdd(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right)
    {
        return new VectorAddVectorExpression(left, right);
    }

    /// <summary>
    /// Subtracts the second vectorized expression from the first element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise subtraction.</returns>
    public static IGpuBigIntVectorExpression ZipSubtract(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right)
    {
        return new VectorSubVectorExpression(left, right);
    }

    /// <summary>
    /// Multiplies the first vectorized expression by the second element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise multiplication.</returns>
    public static IGpuBigIntVectorExpression ZipMultiply(
        this IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right)
    {
        return new VectorMulVectorExpression(left, right);
    }

    /// <summary>
    /// Computes the sum of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel sum reduction.</returns>
    public static GpuBigIntExpr Sum(this IGpuBigIntVectorExpression source)
    {
        return new GpuBigIntExpr(new VectorSumReductionExpression(source));
    }

    /// <summary>
    /// Computes the maximum value of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel max reduction.</returns>
    public static GpuBigIntExpr Max(this IGpuBigIntVectorExpression source)
    {
        return new GpuBigIntExpr(new VectorMaxReductionExpression(source));
    }

    /// <summary>
    /// Computes the minimum value of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the parallel min reduction.</returns>
    public static GpuBigIntExpr Min(this IGpuBigIntVectorExpression source)
    {
        return new GpuBigIntExpr(new VectorMinReductionExpression(source));
    }

    /// <summary>
    /// Computes the number of elements in the vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>A deferred expression tree representing the element count.</returns>
    public static GpuBigIntExpr Count(this IGpuBigIntVectorExpression source)
    {
        return new GpuBigIntExpr(new VectorCountExpression(source));
    }

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
    public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.Equal);

    /// <summary>
    /// Evaluates if each element is not equal to a scalar.
    /// </summary>
    public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.NotEqual);

    /// <summary>
    /// Evaluates if each element is greater than a scalar.
    /// </summary>
    public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.GreaterThan);

    /// <summary>
    /// Evaluates if each element is greater than or equal to a scalar.
    /// </summary>
    public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.GreaterThanOrEqual);

    /// <summary>
    /// Evaluates if each element is less than a scalar.
    /// </summary>
    public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.LessThan);

    /// <summary>
    /// Evaluates if each element is less than or equal to a scalar.
    /// </summary>
    public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar)
        => new VectorCompareScalarExpression(source, new GpuBigIntLeafExpression(scalar), VectorCompareOperator.LessThanOrEqual);

    /// <summary>
    /// Evaluates if elements in the left vector are equal to the right vector element-wise.
    /// </summary>
    public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.Equal);

    /// <summary>
    /// Evaluates if elements in the left vector are not equal to the right vector element-wise.
    /// </summary>
    public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.NotEqual);

    /// <summary>
    /// Evaluates if elements in the left vector are greater than the right vector element-wise.
    /// </summary>
    public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.GreaterThan);

    /// <summary>
    /// Evaluates if elements in the left vector are greater than or equal to the right vector element-wise.
    /// </summary>
    public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.GreaterThanOrEqual);

    /// <summary>
    /// Evaluates if elements in the left vector are less than the right vector element-wise.
    /// </summary>
    public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.LessThan);

    /// <summary>
    /// Evaluates if elements in the left vector are less than or equal to the right vector element-wise.
    /// </summary>
    public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right)
        => new VectorCompareVectorExpression(left, right, VectorCompareOperator.LessThanOrEqual);

    /// <summary>
    /// Performs an element-wise logical AND on two boolean vector expressions.
    /// </summary>
    public static IGpuBoolVectorExpression And(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right)
        => new VectorLogicalExpression(left, right, isAnd: true);

    /// <summary>
    /// Performs an element-wise logical OR on two boolean vector expressions.
    /// </summary>
    public static IGpuBoolVectorExpression Or(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right)
        => new VectorLogicalExpression(left, right, isAnd: false);

    /// <summary>
    /// Performs an element-wise logical NOT on a boolean vector expression.
    /// </summary>
    public static IGpuBoolVectorExpression Not(this IGpuBoolVectorExpression source)
        => new VectorNotExpression(source);

    /// <summary>
    /// Determines whether any element of a boolean vector expression evaluates to true.
    /// </summary>
    public static IGpuBoolExpr Any(this IGpuBoolVectorExpression source)
        => new VectorAnyTrueExpression(source);

    /// <summary>
    /// Determines whether all elements of a boolean vector expression evaluate to true.
    /// </summary>
    public static IGpuBoolExpr All(this IGpuBoolVectorExpression source)
        => new VectorAllTrueExpression(source);
}
