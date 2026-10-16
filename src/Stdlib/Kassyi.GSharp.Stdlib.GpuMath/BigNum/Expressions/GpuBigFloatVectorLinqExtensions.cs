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

    /// <summary>
    /// Adds a scalar <see cref="GpuBigFloat"/> to each element of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to add to each element.</param>
    /// <returns>A new vectorized expression representing the scalar addition.</returns>
    public static IGpuBigFloatVectorExpression SelectAdd(
        this IGpuBigFloatVectorExpression source,
        GpuBigFloat scalar) =>
        new FloatVectorAddScalarExpression(source, scalar);

    /// <summary>
    /// Subtracts a scalar <see cref="GpuBigFloat"/> from each element of a vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to subtract from each element.</param>
    /// <returns>A new vectorized expression representing the scalar subtraction.</returns>
    public static IGpuBigFloatVectorExpression SelectSubtract(
        this IGpuBigFloatVectorExpression source,
        GpuBigFloat scalar) =>
        new FloatVectorSubScalarExpression(source, scalar);

    /// <summary>
    /// Multiplies each element of a vectorized expression by a scalar <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="scalar">The scalar value to multiply each element by.</param>
    /// <returns>A new vectorized expression representing the scalar multiplication.</returns>
    public static IGpuBigFloatVectorExpression SelectMultiply(
        this IGpuBigFloatVectorExpression source,
        GpuBigFloat scalar) =>
        new FloatVectorMulScalarExpression(source, scalar);

    /// <summary>
    /// Divides each element of a vectorized expression by a scalar <see cref="GpuBigFloat"/>.
    /// </summary>
    /// <param name="source">The input vectorized expression (dividend).</param>
    /// <param name="scalar">The scalar divisor value.</param>
    /// <returns>A new vectorized expression representing the scalar division.</returns>
    public static IGpuBigFloatVectorExpression SelectDivide(
        this IGpuBigFloatVectorExpression source,
        GpuBigFloat scalar) =>
        new FloatVectorDivScalarExpression(source, scalar);

    /// <summary>
    /// Adds two vectorized floating-point expressions element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise addition.</returns>
    public static IGpuBigFloatVectorExpression ZipAdd(
        this IGpuBigFloatVectorExpression left,
        IGpuBigFloatVectorExpression right) =>
        new FloatVectorAddVectorExpression(left, right);

    /// <summary>
    /// Subtracts the second vectorized floating-point expression from the first element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise subtraction.</returns>
    public static IGpuBigFloatVectorExpression ZipSubtract(
        this IGpuBigFloatVectorExpression left,
        IGpuBigFloatVectorExpression right) =>
        new FloatVectorSubVectorExpression(left, right);

    /// <summary>
    /// Multiplies the first vectorized floating-point expression by the second element-wise.
    /// </summary>
    /// <param name="left">The first vectorized expression.</param>
    /// <param name="right">The second vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise multiplication.</returns>
    public static IGpuBigFloatVectorExpression ZipMultiply(
        this IGpuBigFloatVectorExpression left,
        IGpuBigFloatVectorExpression right) =>
        new FloatVectorMulVectorExpression(left, right);

    /// <summary>
    /// Divides the first vectorized floating-point expression by the second element-wise.
    /// </summary>
    /// <param name="left">The dividend vectorized expression.</param>
    /// <param name="right">The divisor vectorized expression.</param>
    /// <returns>A new vectorized expression representing the element-wise division.</returns>
    public static IGpuBigFloatVectorExpression ZipDivide(
        this IGpuBigFloatVectorExpression left,
        IGpuBigFloatVectorExpression right) =>
        new FloatVectorDivVectorExpression(left, right);

    /// <summary>
    /// Bypasses a specified number of elements in a vectorized sequence and then returns the remaining elements.
    /// </summary>
    /// <param name="source">An <see cref="IGpuBigFloatVectorExpression"/> to return elements from.</param>
    /// <param name="count">The number of elements to skip before returning the remaining elements.</param>
    /// <returns>An <see cref="IGpuBigFloatVectorExpression"/> that contains the elements that occur after the specified index.</returns>
    public static IGpuBigFloatVectorExpression Skip(this IGpuBigFloatVectorExpression source, int count)
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
    /// <param name="source">An <see cref="IGpuBigFloatVectorExpression"/> to return elements from.</param>
    /// <param name="count">The number of elements to return.</param>
    /// <returns>An <see cref="IGpuBigFloatVectorExpression"/> that contains the specified number of elements from the start of the input sequence.</returns>
    public static IGpuBigFloatVectorExpression Take(this IGpuBigFloatVectorExpression source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        count = Math.Max(0, count);
        return new FloatVectorTakeExpression(source, count);
    }

    /// <summary>
    /// Gets the number of elements in the vectorized expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <returns>The element count.</returns>
    public static int Count(this IGpuBigFloatVectorExpression source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Length;
    }

    /// <summary>
    /// Computes the sum of all elements in a vectorized floating-point expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="workspace">The GPU workspace used for evaluation.</param>
    /// <returns>A <see cref="GpuBigFloat"/> containing the sum of all elements.</returns>
    public static GpuBigFloat Sum(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(workspace);

        var evaluated = source.Evaluate(workspace);
        if (evaluated.Length == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        if (evaluated.Length == 1)
        {
            var first = evaluated.GetElement(0);
            var copySig = new GpuBigUInt(workspace, first.UFloat.Significand.NumWords);
            workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, first.UFloat.Significand.DevicePointer, copySig.NumWords, workspace.Stream);
            return new GpuBigFloat(copySig, first.UFloat.Exponent, first.IsNegative);
        }

        GpuBigFloat acc = evaluated.GetElement(0) + evaluated.GetElement(1);
        try
        {
            for (var i = 2; i < evaluated.Length; i++)
            {
                GpuBigFloat nextAcc = acc + evaluated.GetElement(i);
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
    /// Computes the maximum value in a vectorized floating-point expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="workspace">The GPU workspace used for evaluation.</param>
    /// <returns>A <see cref="GpuBigFloat"/> containing the maximum value.</returns>
    public static GpuBigFloat Max(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(workspace);

        var evaluated = source.Evaluate(workspace);
        if (evaluated.Length == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        var best = evaluated.GetElement(0);
        for (var i = 1; i < evaluated.Length; i++)
        {
            var current = evaluated.GetElement(i);
            if (current > best)
            {
                best = current;
            }
        }

        var copySig = new GpuBigUInt(workspace, best.UFloat.Significand.NumWords);
        workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, best.UFloat.Significand.DevicePointer, copySig.NumWords, workspace.Stream);
        return new GpuBigFloat(copySig, best.UFloat.Exponent, best.IsNegative);
    }

    /// <summary>
    /// Computes the minimum value in a vectorized floating-point expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="workspace">The GPU workspace used for evaluation.</param>
    /// <returns>A <see cref="GpuBigFloat"/> containing the minimum value.</returns>
    public static GpuBigFloat Min(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(workspace);

        var evaluated = source.Evaluate(workspace);
        if (evaluated.Length == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        var best = evaluated.GetElement(0);
        for (var i = 1; i < evaluated.Length; i++)
        {
            var current = evaluated.GetElement(i);
            if (current < best)
            {
                best = current;
            }
        }

        var copySig = new GpuBigUInt(workspace, best.UFloat.Significand.NumWords);
        workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, best.UFloat.Significand.DevicePointer, copySig.NumWords, workspace.Stream);
        return new GpuBigFloat(copySig, best.UFloat.Exponent, best.IsNegative);
    }

    /// <summary>
    /// Computes the average of all elements in a vectorized floating-point expression.
    /// </summary>
    /// <param name="source">The input vectorized expression.</param>
    /// <param name="workspace">The GPU workspace used for evaluation.</param>
    /// <returns>A <see cref="GpuBigFloat"/> containing the average value.</returns>
    public static GpuBigFloat Average(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace)
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
    /// <param name="source">The vectorized expression to evaluate.</param>
    /// <param name="workspace">The GPU workspace used for evaluation.</param>
    /// <param name="destination">The pre-allocated destination array.</param>
    public static void EvaluateTo(
        this IGpuBigFloatVectorExpression source,
        GpuWorkspace workspace,
        GpuBigFloatArray destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(workspace);

        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Destination array is smaller than the source expression length.", nameof(destination));
        }

        source.Evaluate(workspace, 0, source.Length, destination);
    }
}
