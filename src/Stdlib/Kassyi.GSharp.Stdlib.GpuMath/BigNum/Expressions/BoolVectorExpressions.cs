using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public enum VectorCompareOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5
}

internal class VectorCompareScalarExpression([TakesOwnership] IGpuBigIntVectorExpression vector, [TakesOwnership] IGpuBigIntExpression scalar, VectorCompareOperator opType) : IGpuBoolVectorExpression
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

        int gridDimX = Length;
        int blockDimX = Math.Min(1024, vector.WordsPerElement);

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

internal class VectorCompareVectorExpression([TakesOwnership] IGpuBigIntVectorExpression left, [TakesOwnership] IGpuBigIntVectorExpression right, VectorCompareOperator opType) : IGpuBoolVectorExpression
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

        int words = Math.Max(left.WordsPerElement, right.WordsPerElement);

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        int gridDimX = Length;
        int blockDimX = Math.Min(1024, words);

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

internal class VectorLogicalExpression([TakesOwnership] IGpuBoolVectorExpression left, [TakesOwnership] IGpuBoolVectorExpression right, bool isAnd) : IGpuBoolVectorExpression
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

        int gridDimX = (Length + 255) / 256;
        int blockDimX = 256;

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

        int gridDimX = (Length + 255) / 256;
        int blockDimX = 256;

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

        int gridDimX = (source.Length + 255) / 256;
        int blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorAnyTrueKernelPtr(
            result.DevicePointer,
            tempResult.DevicePointer,
            source.Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        var hResult = new uint[1];
        workspace.Provider.CopyDeviceToHost(hResult, tempResult.DevicePointer, 1, workspace.Stream);

        return hResult[0] != 0;
    }

    public void Dispose()
    {
        source.Dispose();
    }
}

internal class VectorAllTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);
        
        using var tempResult = new GpuBigUIntArray(workspace, 1, 1);
        
        // Initialize with 1
        var initVal = new uint[] { 1u };
        workspace.Provider.CopyHostToDevice(tempResult.DevicePointer, initVal, 1, workspace.Stream);

        int gridDimX = (source.Length + 255) / 256;
        int blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorAllTrueKernelPtr(
            result.DevicePointer,
            tempResult.DevicePointer,
            source.Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        var hResult = new uint[1];
        workspace.Provider.CopyDeviceToHost(hResult, tempResult.DevicePointer, 1, workspace.Stream);

        return hResult[0] != 0;
    }

    public void Dispose()
    {
        source.Dispose();
    }
}

public interface IGpuBoolExpr : System.IDisposable
{
    bool Evaluate(GpuWorkspace workspace);
}
