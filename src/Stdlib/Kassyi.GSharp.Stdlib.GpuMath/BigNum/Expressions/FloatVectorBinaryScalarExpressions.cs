namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly record struct FloatVectorScalarOperands(
    GpuBigFloatArray Vector,
    GpuFloatOperand Scalar,
    GpuBigFloatArray Destination);

internal abstract class FloatVectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    int wordsPerElement) : IGpuBigFloatVectorExpression
{
    protected const int DefaultDivShiftBits = 32;

    public int Length => vector.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    private GpuBigFloatArray _result;
    private bool _allocated;

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

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

        var operands = new FloatVectorScalarOperands(vecResult, scalar, destination);
        DispatchKernel(workspace, operands, (uint)evalCount);
        return destination;
    }

    protected static GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation CreateScalarFloatOp(FloatVectorScalarOperands operands)
    {
        var scalarSig = operands.Scalar.Significand;
        return new GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation
        {
            Core = new GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation
            {
                Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = operands.Vector.Significands.DevicePointer,
                    Signs = operands.Vector.Signs.DevicePointer,
                    NumWords = operands.Vector.WordsPerElement
                },
                Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = scalarSig.DevicePointer,
                    Signs = operands.Vector.Signs.DevicePointer,
                    NumWords = scalarSig.NumWords
                },
                Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
                {
                    Limbs = operands.Destination.Significands.DevicePointer,
                    Signs = operands.Destination.Signs.DevicePointer,
                    NumWords = operands.Destination.WordsPerElement
                },
                ScalarSign = operands.Scalar.IsNegative ? 1u : 0u,
                IsScalar = true
            },
            Exponents = new GpuBigIntVectorKernels.AotContexts.FloatVectorExponents
            {
                ExpA = operands.Vector.Exponents.DevicePointer,
                ExpB = operands.Vector.Exponents.DevicePointer,
                ExpResult = operands.Destination.Exponents.DevicePointer,
                ScalarExp = operands.Scalar.Exponent
            }
        };
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX);

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

internal sealed class FloatVectorAddScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateScalarFloatOp(operands),
            OpSign = 0u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatAddScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorSubScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateScalarFloatOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorMulScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        vector.WordsPerElement + scalar.Significand.NumWords)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatMul
        {
            FloatOp = CreateScalarFloatOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorFloatMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorDivScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatDiv
        {
            FloatOp = CreateScalarFloatOp(operands),
            PreShiftBits = DefaultDivShiftBits
        };
        GpuBigIntVectorKernels.LaunchVectorFloatDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorPowScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand exponentScalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        exponentScalar,
        Math.Max(vector.WordsPerElement, exponentScalar.Significand.NumWords))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var expOperand = operands.Scalar;
        FloatVectorElementwiseHelper.ExecuteUnary(
            workspace,
            operands.Vector,
            operands.Destination,
            (int)gridDimX,
            (ref GpuBigIntExecutionContext ctx, IGpuBigFloatArithmetic arith, in GpuFloatOperand src, GpuBigIntMemoryHandle dst) =>
                arith.Pow(ref ctx, in src, in expOperand, dst));
    }
}
