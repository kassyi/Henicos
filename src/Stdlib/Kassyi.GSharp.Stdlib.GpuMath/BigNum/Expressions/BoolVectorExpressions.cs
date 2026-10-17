namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Specifies the type of comparison operation between GPU vector elements.
/// </summary>
public enum VectorCompareOperator
{
    /// <summary>Tests for equality (==).</summary>
    Equal = 0,

    /// <summary>Tests for inequality (!=).</summary>
    NotEqual = 1,

    /// <summary>Tests for strictly greater than (&gt;).</summary>
    GreaterThan = 2,

    /// <summary>Tests for greater than or equal (&gt;=).</summary>
    GreaterThanOrEqual = 3,

    /// <summary>Tests for strictly less than (&lt;).</summary>
    LessThan = 4,

    /// <summary>Tests for less than or equal (&lt;=).</summary>
    LessThanOrEqual = 5
}

internal class VectorCompareScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => vector.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        var vecResult = vector.Evaluate(workspace);
        var scalarResult = scalar.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        var gridDimX = Length;
        var blockDimX = Math.Min(1024, vector.WordsPerElement);

        GpuBigIntCmpKernels.LaunchVectorCompareScalarKernelPtr(
            vecResult.Values.DevicePointer,
            scalarResult.Value.DevicePointer,
            _finalValues.DevicePointer,
            vector.WordsPerElement,
            (int)opType,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return _finalValues;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        vector.Dispose();
    }
}

internal class VectorCompareVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => left.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Lengths must match");
        }

        var words = Math.Max(left.WordsPerElement, right.WordsPerElement);

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        var gridDimX = Length;
        var blockDimX = Math.Min(1024, words);

        GpuBigIntCmpKernels.LaunchVectorCompareVectorKernelPtr(
            leftResult.Values.DevicePointer,
            rightResult.Values.DevicePointer,
            _finalValues.DevicePointer,
            words,
            (int)opType,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return _finalValues;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuBigFloat scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => vector.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        var vecResult = vector.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        if (Length == 0)
        {
            return _finalValues;
        }

        var scalarSig = scalar.UFloat.Significand;
        var maxWords = Math.Max(vecResult.WordsPerElement, scalarSig.NumWords);
        var blockDimX = (uint)Math.Clamp(maxWords, 32, 1024);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            A = vecResult.Significands.DevicePointer,
            B = scalarSig.DevicePointer,
            Result = _finalValues.DevicePointer,
            ExpA = vecResult.Exponents.DevicePointer,
            ExpB = vecResult.Exponents.DevicePointer,
            SignA = vecResult.Signs.DevicePointer,
            SignB = vecResult.Signs.DevicePointer,
            NumWordsA = vecResult.WordsPerElement,
            NumWordsB = scalarSig.NumWords,
            ScalarExp = scalar.UFloat.Exponent,
            ScalarSign = scalar.IsNegative ? 1u : 0u,
            IsScalar = true,
            OpType = (int)opType
        };

        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

        return _finalValues;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        vector.Dispose();
    }
}

internal sealed class FloatVectorCompareVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => left.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Lengths must match");
        }

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        if (Length == 0)
        {
            return _finalValues;
        }

        var maxWords = Math.Max(leftResult.WordsPerElement, rightResult.WordsPerElement);
        var blockDimX = (uint)Math.Clamp(maxWords, 32, 1024);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            A = leftResult.Significands.DevicePointer,
            B = rightResult.Significands.DevicePointer,
            Result = _finalValues.DevicePointer,
            ExpA = leftResult.Exponents.DevicePointer,
            ExpB = rightResult.Exponents.DevicePointer,
            SignA = leftResult.Signs.DevicePointer,
            SignB = rightResult.Signs.DevicePointer,
            NumWordsA = leftResult.WordsPerElement,
            NumWordsB = rightResult.WordsPerElement,
            ScalarExp = 0,
            ScalarSign = 0u,
            IsScalar = false,
            OpType = (int)opType
        };

        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

        return _finalValues;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal class VectorLogicalExpression(
    [TakesOwnership] IGpuBoolVectorExpression left,
    [TakesOwnership] IGpuBoolVectorExpression right,
    bool isAnd) : IGpuBoolVectorExpression
{
    public int Length => left.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Lengths must match");
        }

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        var gridDimX = (Length + 255) / 256;
        var blockDimX = 256;

        if (isAnd)
        {
            GpuBigIntCmpKernels.LaunchVectorAndKernelPtr(
                leftResult.DevicePointer,
                rightResult.DevicePointer,
                _finalValues.DevicePointer,
                Length,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }
        else
        {
            GpuBigIntCmpKernels.LaunchVectorOrKernelPtr(
                leftResult.DevicePointer,
                rightResult.DevicePointer,
                _finalValues.DevicePointer,
                Length,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }

        return _finalValues;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal class VectorNotExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolVectorExpression
{
    public int Length => source.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        var gridDimX = (Length + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorNotKernelPtr(
            result.DevicePointer,
            _finalValues.DevicePointer,
            Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return _finalValues;
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        source.Dispose();
    }
}

internal class VectorAnyTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);

        using var tempResult = new GpuBigUIntArray(workspace, 1, 1);
        workspace.Provider.ClearMemoryAsync(tempResult.DevicePointer, 1, workspace.Stream);

        var gridDimX = (source.Length + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorAnyTrueKernelPtr(
            result.DevicePointer,
            tempResult.DevicePointer,
            source.Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return workspace.Provider.ReadWord(tempResult.DevicePointer, 0, workspace.Stream) != 0;
    }

    public void Dispose() => source.Dispose();
}

internal class VectorAllTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);

        using var tempResult = new GpuBigUIntArray(workspace, 1, 1);

        // Initialize with 1
        workspace.Provider.WriteWord(tempResult.DevicePointer, 0, 1u, workspace.Stream);

        var gridDimX = (source.Length + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorAllTrueKernelPtr(
            result.DevicePointer,
            tempResult.DevicePointer,
            source.Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return workspace.Provider.ReadWord(tempResult.DevicePointer, 0, workspace.Stream) != 0;
    }

    public void Dispose() => source.Dispose();
}

/// <summary>
/// Represents a deferred scalar boolean expression on the GPU.
/// </summary>
public interface IGpuBoolExpr : IDisposable
{
    /// <summary>
    /// Evaluates the boolean expression on the GPU and copies the scalar boolean result to host.
    /// </summary>
    /// <param name="workspace">The GPU workspace memory context.</param>
    /// <returns><c>true</c> if the condition evaluated to true; otherwise, <c>false</c>.</returns>
    bool Evaluate(GpuWorkspace workspace);
}
