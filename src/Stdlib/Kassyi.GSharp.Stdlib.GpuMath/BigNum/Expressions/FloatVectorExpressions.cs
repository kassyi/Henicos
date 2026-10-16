namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an expression node that evaluates to a contiguous array of GPU arbitrary-precision floating-point numbers.
/// </summary>
public interface IGpuBigFloatVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Gets the number of 32-bit significand words per element.
    /// </summary>
    int WordsPerElement { get; }

    /// <summary>
    /// Evaluates the vectorized floating-point expression into a new or existing device array, optionally computing only a sub-slice.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate. If null, evaluates to the end of the vector length.</param>
    /// <param name="targetResult">An optional destination array for the evaluated values. If provided, it must have a length of elementCount.</param>
    /// <returns>A <see cref="GpuBigFloatArray"/> containing the evaluated vector slice results.</returns>
    GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null);
}

internal sealed class GpuBigFloatVectorLeafExpression(GpuBigFloatArray array) : IGpuBigFloatVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        var slice = (elementOffset == 0 && count == Length)
            ? array
            : array.Slice(elementOffset, count);

        if (targetResult != null)
        {
            var dest = targetResult.Value;
            for (var i = 0; i < count; i++)
            {
                dest.SetElement(i, slice.GetElement(i));
            }

            return dest;
        }

        return slice;
    }

    public void Dispose()
    {
        // Leaf expression does not own the source array.
    }
}

internal sealed class FloatVectorBinaryScalarExpression<TOp>(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuBigFloat scalar,
    int wordsPerElement) : IGpuBigFloatVectorExpression
    where TOp : struct, IFloatOperator<GpuBigFloat>
{
    public int Length => vector.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    private GpuBigFloatArray _result;
    private bool _allocated;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        var vecResult = vector.Evaluate(workspace, elementOffset, evalCount);

        GpuBigFloatArray destination;
        if (targetResult != null)
        {
            destination = targetResult.Value;
        }
        else
        {
            if (!_allocated)
            {
                _result = new GpuBigFloatArray(workspace, evalCount, WordsPerElement);
                _allocated = true;
            }

            destination = _result;
        }

        var visitor = new GpuBigFloatExpressionVisitor(workspace);
        for (var i = 0; i < evalCount; i++)
        {
            var elem = vecResult.GetElement(i);
            using var res = TOp.Execute(elem, scalar, ref visitor);
            destination.SetElement(i, res);
        }

        return destination;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _result.Dispose();
            _allocated = false;
        }

        vector.Dispose();
    }
}

internal sealed class FloatVectorBinaryVectorExpression<TOp>(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    int wordsPerElement) : IGpuBigFloatVectorExpression
    where TOp : struct, IFloatOperator<GpuBigFloat>
{
    public int Length => left.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    private GpuBigFloatArray _result;
    private bool _allocated;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        var leftResult = left.Evaluate(workspace, elementOffset, evalCount);
        var rightResult = right.Evaluate(workspace, elementOffset, evalCount);

        if (leftResult.Length != rightResult.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        GpuBigFloatArray destination;
        if (targetResult != null)
        {
            destination = targetResult.Value;
        }
        else
        {
            if (!_allocated)
            {
                _result = new GpuBigFloatArray(workspace, evalCount, WordsPerElement);
                _allocated = true;
            }

            destination = _result;
        }

        var visitor = new GpuBigFloatExpressionVisitor(workspace);
        for (var i = 0; i < evalCount; i++)
        {
            var lElem = leftResult.GetElement(i);
            var rElem = rightResult.GetElement(i);
            using var res = TOp.Execute(lElem, rElem, ref visitor);
            destination.SetElement(i, res);
        }

        return destination;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _result.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression
{
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Length);

    public int Length => inner.Length - _effectiveSkip;
    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        return inner.Evaluate(workspace, _effectiveSkip + elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression
{
    private readonly int _effectiveTake = Math.Clamp(takeCount, 0, inner.Length);

    public int Length => _effectiveTake;
    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        return inner.Evaluate(workspace, elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}
