namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorCompareScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IDeviceCountedBoolVector
{
    public int Length => vector.Length;
    public int Capacity => vector.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => vector.TryGetLength(out length);

    // The flags of the capacity; the elements are the first Count of them, as for the input (design D3 4.3)
    public BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace)
    {
        var vecResult = DeviceCounted.Evaluate(vector, workspace);
        var scalarResult = scalar.Evaluate(workspace);
        try
        {
            var capacity = Capacity;
            if (!_allocated)
            {
                _finalValues = new GpuBigUIntArray(workspace, capacity, 1);
                _allocated = true;
            }

            // The scalar is read with its own word count and sign: it may be narrower or wider than the elements, and
            // it may be a view into a larger buffer whose following words are not 0.
            var ctx = new GpuBigIntCmpKernels.AotContexts.VectorCompare
            {
                Left = VectorCompareLaunch.Operand(vecResult.Storage),
                Right = new GpuBigIntCmpKernels.AotContexts.SignedCompareOperand
                {
                    Limbs = scalarResult.Value.DevicePointer,
                    Signs = IntPtr.Zero,
                    NumWords = scalarResult.Value.NumWords,
                    Stride = 0,
                    ScalarSign = scalarResult.IsNegative ? 1u : 0u
                },
                Result = _finalValues.DevicePointer,
                OpType = (int)opType
            };

            VectorCompareLaunch.Launch(workspace, ctx, capacity, vecResult.Storage.WordsPerElement,
                scalarResult.Value.NumWords);

            return new BoolVectorEvaluation(_finalValues, vecResult.Count);
        }
        finally
        {
            // An owned scalar result (a compound scalar expression) goes back to the pool; a leaf's is borrowed
            scalarResult.Dispose();
        }
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
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IDeviceCountedBoolVector
{
    public int Length => left.Length;
    public int Capacity => left.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => left.TryGetLength(out length);

    public BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace)
    {
        if (left.Capacity != right.Capacity)
        {
            throw new ArgumentException("Lengths must match");
        }

        var leftResult = DeviceCounted.Evaluate(left, workspace);
        var rightResult = DeviceCounted.Evaluate(right, workspace);
        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);

        var capacity = Capacity;
        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, capacity, 1);
            _allocated = true;
        }

        // Each side is read with its own stride and word count (the two may differ).
        var ctx = new GpuBigIntCmpKernels.AotContexts.VectorCompare
        {
            Left = VectorCompareLaunch.Operand(leftResult.Storage),
            Right = VectorCompareLaunch.Operand(rightResult.Storage),
            Result = _finalValues.DevicePointer,
            OpType = (int)opType
        };

        VectorCompareLaunch.Launch(workspace, ctx, capacity, leftResult.Storage.WordsPerElement,
            rightResult.Storage.WordsPerElement);

        return new BoolVectorEvaluation(_finalValues, count);
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

internal static class VectorCompareLaunch
{
    public static GpuBigIntCmpKernels.AotContexts.SignedCompareOperand Operand(GpuBigIntArrayView vector) => new()
    {
        Limbs = vector.Values.DevicePointer,
        Signs = vector.Signs.DevicePointer,
        NumWords = vector.WordsPerElement,
        Stride = vector.WordsPerElement,
        ScalarSign = 0u
    };

    // One block per element, as wide as the wider side rounded up to whole warps (at most 1024 threads), so that the
    // block-wide reductions of the kernel never see a partial warp; the extra lanes read no word. An empty vector
    // launches nothing: a grid of 0 blocks is an invalid launch.
    public static void Launch(
        GpuWorkspace workspace,
        GpuBigIntCmpKernels.AotContexts.VectorCompare ctx,
        int length,
        int leftWords,
        int rightWords)
    {
        if (length == 0)
        {
            return;
        }

        var words = Math.Max(leftWords, rightWords);
        var blockDimX = Math.Min(1024, (words + 31) & ~31);
        GpuBigIntCmpKernels.LaunchVectorCompareKernelPtr(
            ctx,
            (uint)length, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);
    }
}
